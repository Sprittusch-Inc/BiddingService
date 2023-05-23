using Bidding.Models;
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;
using MongoDB.Driver;

namespace Bidding;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IMongoCollection<Auction> _collection;
    protected static IMongoClient _client;
    protected static IMongoDatabase _db;
    private Vault vault = new();
    

    public Worker(ILogger<Worker> logger)
    {
        string cons = vault.GetSecret("dbconnection", "auctiondb").Result;

        _client = new MongoClient(cons);
        _db = _client.GetDatabase("AuctionDB");
        var _collection = _db.GetCollection<Auction>("Auctions");
        _logger = logger;

    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
         _logger.LogInformation("Worker running at: {time}", DateTime.UtcNow);
        while (!stoppingToken.IsCancellationRequested)
        {
           

            var factory = new ConnectionFactory { HostName = "localhost" };
            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            channel.QueueDeclare(queue: "bids",
                                 durable: false,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

            Console.WriteLine(" [*] Waiting for messages.");

            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                Bid? bid = JsonSerializer.Deserialize<Bid>(message);

                try
                {
                    var filter = Builders<Auction>.Filter.Eq("AuctionId", bid.AuctionId);
                    var update = Builders<Auction>.Update.Push(e => e.bids, bid);
                    _collection.FindOneAndUpdate<Auction>(filter, update);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                    throw;
                }
            };



            await Task.Delay(1000, stoppingToken);
        }
    }
}
