using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using orderManagementSystem.Data;
using orderManagementSystem.Models;

namespace orderManagementSystem.Services;

public class CustomerSessionService
{
    private const string CustomerIdKey = "CurrentCustomerId";
    private const int InactivityMinutes = 10;

    private readonly ApplicationDbContext _context;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CustomerSessionService(
        ApplicationDbContext context,
        IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        _httpContextAccessor = httpContextAccessor;
    }

    private HttpContext CurrentHttpContext =>
        _httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("No active HTTP context.");

    public async Task ExpireInactiveSessionsAsync()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-InactivityMinutes);

        var expired = await _context.Customers
            .Where(c => c.IsActive && c.LastActivityUtc < cutoff)
            .ToListAsync();

        foreach (var customer in expired)
            customer.IsActive = false;

        if (expired.Count > 0)
            await _context.SaveChangesAsync();
    }

    public async Task<Customer?> GetCurrentCustomerAsync()
    {
        var httpContext = CurrentHttpContext;
        await ExpireInactiveSessionsAsync();

        var customerId = httpContext.Session.GetInt32(CustomerIdKey);

        if (customerId == null)
            return null;

        var customer = await _context.Customers
            .FirstOrDefaultAsync(c =>
                c.Id == customerId && c.IsActive);

        if (customer == null)
        {
            httpContext.Session.Remove(CustomerIdKey);
            return null;
        }

        customer.LastActivityUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return customer;
    }

    
    public async Task<Customer> BeginSessionAsync(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name is required.");

        var httpContext = CurrentHttpContext;
        await ExpireInactiveSessionsAsync();

        displayName = displayName.Trim();

        var currentId = httpContext.Session.GetInt32(CustomerIdKey);

        // Reuse the current session if the name is unchanged.
        if (currentId != null)
        {
            var currentCustomer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Id == currentId && c.IsActive);

            if (currentCustomer != null &&
                currentCustomer.DisplayName.Equals(
                    displayName, StringComparison.OrdinalIgnoreCase))
            {
                currentCustomer.LastActivityUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return currentCustomer;
            }

            await EndSessionAsync();
        }

        var now = DateTime.UtcNow;
        var cutoff = now.AddMinutes(-InactivityMinutes);

        // Resume a recently inactive customer within the 10-minute window.
        var returningCustomer = await _context.Customers
            .Where(c =>
                c.DisplayName.ToLower() == displayName.ToLower() &&
                c.LastActivityUtc >= cutoff)
            .OrderByDescending(c => c.LastActivityUtc)
            .FirstOrDefaultAsync();

        if (returningCustomer != null)
        {
            returningCustomer.IsActive = true;
            returningCustomer.LastActivityUtc = now;
            await _context.SaveChangesAsync();

            httpContext.Session.SetInt32(CustomerIdKey, returningCustomer.Id);
            return returningCustomer;
        }

        // No recent record: create a fresh customer.
        var customer = new Customer
        {
            DisplayName = displayName,
            IsActive = true,
            CreatedAtUtc = now,
            LastActivityUtc = now
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        httpContext.Session.SetInt32(CustomerIdKey, customer.Id);

        return customer;
    }


    public async Task EndSessionAsync()
    {
        var httpContext = CurrentHttpContext;
        var customerId = httpContext.Session.GetInt32(CustomerIdKey);

        if (customerId != null)
        {
            var customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Id == customerId);

            if (customer != null && customer.IsActive)
            {
                customer.IsActive = false;
                await _context.SaveChangesAsync();
            }
        }

        httpContext.Session.Remove(CustomerIdKey);
    }
}