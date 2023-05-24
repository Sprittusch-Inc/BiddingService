using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Bidding.Models;

public class Auction
{
    [BsonElement("_id")]
    [BsonId]
    public ObjectId Id { get; set; }
    public int AuctionId { get; set; }
    public string? AuctioneerId { get; set; }
    public int ItemId { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int MinBid { get; set; }
    public int NextBid { get; set; }
    public List<Bid>? Bids { get; set; }
}