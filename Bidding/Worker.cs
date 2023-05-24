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
    private readonly IConfiguration _config;
    private static string? _hostName;
    private readonly IMongoCollection<Auction> _collection;
    protected static IMongoClient _client;
    protected static IMongoDatabase _db;
    private Vault vault = new();


    public Worker(ILogger<Worker> logger, IConfiguration config)
    {
        // string cons = vault.GetSecret("dbconnection", "auctiondb").Result;
        string cons = "mongodb://localhost:27017";
        
        _logger = logger;
        _config = config;

        _hostName = config["HostName"] ?? "localhost";
        _logger.LogInformation($"HostName is set to: {_hostName}");

        _client = new MongoClient(cons);
        _db = _client.GetDatabase("AuctionDB");
        _collection = _db.GetCollection<Auction>("Auctions");

    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker running at: {time}", DateTime.UtcNow);
        var factory = new ConnectionFactory { HostName = _hostName };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.QueueDeclare(
            queue: "bids",
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                Bid? bid = JsonSerializer.Deserialize<Bid>(message);
                _logger.LogInformation($"Received Bid with BidId: {bid?.Id}");

                try
                {
                    var filter = Builders<Auction>.Filter.Eq("AuctionId", bid?.AuctionId);
                    var update = Builders<Auction>.Update.Push(e => e.Bids, bid);
                    _collection.FindOneAndUpdate<Auction>(filter, update);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                    throw;
                }
            };

            channel.BasicConsume(
                queue: "bids",
                autoAck: true,
                consumer: consumer
            );


            await Task.Delay(1000, stoppingToken);
        }
    }
}
