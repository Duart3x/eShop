using eShop.Basket.API.Grpc;
using GrpcBasketItem = eShop.Basket.API.Grpc.BasketItem;
using GrpcBasketClient = eShop.Basket.API.Grpc.Basket.BasketClient;
using System.Diagnostics;
using System.Text.Json;

namespace eShop.WebApp.Services;

public class BasketService(GrpcBasketClient basketClient)
{
    public async Task<IReadOnlyCollection<BasketQuantity>> GetBasketAsync()
    {
        var result = await basketClient.GetBasketAsync(new());
        return MapToBasket(result);
    }

    public async Task DeleteBasketAsync()
    {
        await basketClient.DeleteBasketAsync(new DeleteBasketRequest());
    }

    public async Task UpdateBasketAsync(IReadOnlyCollection<BasketQuantity> basket)
    {
        using var activity = Activity.Current;
        
        var updatePayload = new UpdateBasketRequest();
        foreach (var item in basket)
        {
            updatePayload.Items.Add(new GrpcBasketItem
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = (double)item.UnitPrice// Adicionado UnitPrice
            });
        }

        activity?.SetTag("new_items", JsonSerializer.Serialize(updatePayload.Items.Select(i => new { i.ProductId, i.Quantity, i.UnitPrice })));

        await basketClient.UpdateBasketAsync(updatePayload);
    }

    private static List<BasketQuantity> MapToBasket(CustomerBasketResponse response)
    {
        var result = new List<BasketQuantity>();
        foreach (var item in response.Items)
        {
            result.Add(new BasketQuantity(item.ProductId, item.Quantity, (decimal)item.UnitPrice)); // Adicionado UnitPrice
        }

        return result;
    }
}

public record BasketQuantity(int ProductId, int Quantity, decimal UnitPrice);

