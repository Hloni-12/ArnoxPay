using System.Text.Json;
using ArnoxPay.Models.ViewModels;
using Microsoft.AspNetCore.Http;

namespace ArnoxPay.Infrastructure;

public interface IServiceCartStore
{
    List<CartLine> GetLines();
    void SetLines(List<CartLine> lines);
    void Add(CartLine line);
    void Remove(string lineId);
    void Clear();
    decimal Total();
    int Count();
}

public sealed class ServiceCartStore(IHttpContextAccessor accessor) : IServiceCartStore
{
    private const string Key = "ArnoxPay.ServiceCart";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private ISession Session => accessor.HttpContext?.Session
        ?? throw new InvalidOperationException("HTTP session is not available.");

    public List<CartLine> GetLines()
    {
        var raw = Session.GetString(Key);
        if (string.IsNullOrEmpty(raw)) return new List<CartLine>();
        try
        {
            return JsonSerializer.Deserialize<List<CartLine>>(raw, JsonOpts) ?? new List<CartLine>();
        }
        catch
        {
            return new List<CartLine>();
        }
    }

    public void SetLines(List<CartLine> lines)
    {
        Session.SetString(Key, JsonSerializer.Serialize(lines, JsonOpts));
    }

    public void Add(CartLine line)
    {
        var list = GetLines();
        list.Add(line);
        SetLines(list);
    }

    public void Remove(string lineId)
    {
        var list = GetLines().Where(x => x.LineId != lineId).ToList();
        SetLines(list);
    }

    public void Clear() => Session.Remove(Key);

    public decimal Total() => GetLines().Sum(x => x.Amount);
    public int Count() => GetLines().Count;
}
