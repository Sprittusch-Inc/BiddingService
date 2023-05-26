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
    protected static IMongoClient? _client;
    protected static IMongoDatabase? _db;
    private Vault vault;


    public Worker(ILogger<Worker> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        _hostName = _config["HostName"] ?? "localhost";
        vault = new Vault(_config);
        string cons = vault.GetSecret("dbconnection", "constring").Result;

        _client = new MongoClient(cons);
        _db = _client.GetDatabase("Auction");
        _collection = _db.GetCollection<Auction>("Auctions");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start af worker og oprettelse af queue med navnet "bids"
        _logger.LogInformation("Worker running at: {time}", DateTime.UtcNow);
        var factory = new ConnectionFactory { HostName = _hostName };
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        channel.QueueDeclare(queue: "bids",
                             durable: true,
                             exclusive: false,
                             autoDelete: false,
                             arguments: null);

        _logger.LogInformation("Successfully declared queue in RabbitMQ");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Oprettelse af consumer som henter beskeder fra køen
            var consumer = new EventingBasicConsumer(channel);
            consumer.Received += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);

                // Deserialisering af besked til et Bid-objekt
                Bid? bid = JsonSerializer.Deserialize<Bid>(message);
                _logger.LogInformation($"Received bid with BidId: {bid?.BidId}");

                // Tjekker om buddet er mindre end nextbid for auktionen givet ved AuctionId i buddet
                Auction auc = _collection.Find(Builders<Auction>.Filter.Eq("AuctionId", bid?.AuctionId)).FirstOrDefault();
                if (bid?.Amount < auc.NextBid)
                {
                    throw new Exception("Bid must be equal to or exceed next bid.");
                }

                // Tjekker om BidId allerede findes i listen af bud på auktionen
                if (auc.Bids.Any(b => b.BidId == bid?.BidId))
                {
                    throw new Exception("BidId already exists.");
                }

                try
                {
                    // Opdaterer listen af bud, hvor buddet tilføjes og nextbid for auktionen sættes til at være buddet + minimumsbud
                    _logger.LogInformation("Updating list of bids and NextBid of auction...");
                    var filter = Builders<Auction>.Filter.Eq("AuctionId", bid?.AuctionId);
                    var update = Builders<Auction>.Update
                        .Push(auction => auction.Bids, bid)
                        .Set(auction => auction.NextBid, bid?.Amount + auc.MinBid);
                    _collection.FindOneAndUpdate<Auction>(filter, update);
                    _logger.LogInformation("List of bids and NextBid of auction updated.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.Message);
                    throw;
                }

            };

            // Basic consume som fjerner beskeden fra køen
            channel.BasicConsume(
                queue: "bids",
                autoAck: true,
                consumer: consumer
            );

            await Task.Delay(1000, stoppingToken);
        }
    }
}
