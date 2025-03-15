using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json.Serialization;
using eShop.Basket.API.Model;

namespace eShop.Basket.API.Repositories;

public class RedisBasketRepository : IBasketRepository
{
    private readonly ILogger<RedisBasketRepository> logger;
    private readonly IDatabase _database;

    // Metrics
    // Sum the value of the items in all baskets
    private readonly UpDownCounter<double> _totalBasketsValue;

    public RedisBasketRepository(ILogger<RedisBasketRepository> logger, IConnectionMultiplexer redis, Meter meter)
    {
        this.logger = logger;
        _database = redis.GetDatabase();

        _totalBasketsValue = meter.CreateUpDownCounter<double>(
            "basket_value_total", 
            description: "Total value of all baskets",
            unit: "USD");

    }

    // implementation:

    // - /basket/{id} "string" per unique basket
    private static RedisKey BasketKeyPrefix = "/basket/"u8.ToArray();
    // note on UTF8 here: library limitation (to be fixed) - prefixes are more efficient as blobs

    private static RedisKey GetBasketKey(string userId) => BasketKeyPrefix.Append(userId);

    public async Task<bool> DeleteBasketAsync(string id)
    {
        using var activity = Activity.Current;

        // get value of the basket 
        var old_basket = await GetBasketAsync(id);
        decimal old_basket_value = 0;
        if (old_basket != null)
        {
            old_basket_value = old_basket.Items.Sum(i => i.Quantity * i.UnitPrice);
        }

        _totalBasketsValue.Add(-(double)old_basket_value);

        // activity?.SetTag("basket", JsonSerializer.Serialize(old_basket));

        return await _database.KeyDeleteAsync(GetBasketKey(id));
    }

    public async Task<CustomerBasket> GetBasketAsync(string customerId)
    {
        using var data = await _database.StringGetLeaseAsync(GetBasketKey(customerId));

        if (data is null || data.Length == 0)
        {
            return null;
        }
        return JsonSerializer.Deserialize(data.Span, BasketSerializationContext.Default.CustomerBasket);
    }

    public async Task<CustomerBasket> UpdateBasketAsync(CustomerBasket basket)
    {
        using var activity = Activity.Current;

        var old_basket = await GetBasketAsync(basket.BuyerId);
        decimal old_basket_value = 0;
        if (old_basket != null)
        {
            old_basket_value = old_basket.Items.Sum(i => i.Quantity * i.UnitPrice);
        }

        decimal new_basket_value = basket.Items.Sum(i => i.Quantity * i.UnitPrice);

        _totalBasketsValue.Add(-(double)old_basket_value);
        _totalBasketsValue.Add((double)new_basket_value);

        var json = JsonSerializer.SerializeToUtf8Bytes(basket, BasketSerializationContext.Default.CustomerBasket);
        // activity?.SetTag("basket", JsonSerializer.Serialize(basket));

        var created = await _database.StringSetAsync(GetBasketKey(basket.BuyerId), json);
        activity?.SetTag("created", created);

        if (!created)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Problem occurred persisting the item");
            activity?.AddEvent(new ActivityEvent("Problem occurred persisting the item"));
            logger.LogInformation("Problem occurred persisting the item.");
            return null;
        }

        logger.LogInformation("Basket item persisted successfully.");
        return await GetBasketAsync(basket.BuyerId);
    }
}

[JsonSerializable(typeof(CustomerBasket))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
public partial class BasketSerializationContext : JsonSerializerContext
{

}
