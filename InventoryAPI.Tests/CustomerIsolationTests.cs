using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories;
using InventoryAPI.Services;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Tests;

public sealed class CustomerIsolationTests
{
    private sealed class FakeTokenProvider : IDecolectaTokenProvider
    {
        public Task<string> GetTokenAsync(bool preferExchangeKey = false) => Task.FromResult(string.Empty);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task CustomerOperations_EnforceCompanyIsolation()
    {
        await using var db = CreateContext();
        var service = new CustomerService(new WorkFlow(db), new HttpClient(), new FakeTokenProvider());

        var createdResult = await service.CreateCustomerAsync(new CustomerWriteDto
        {
            DocumentNumber = "12345678",
            Name = "Cliente Empresa 1",
            Email = "cliente1@empresa.com"
        }, companyId: 1);

        Assert.Equal(ServiceStatus.Created, createdResult.Status);
        var created = Assert.IsType<CustomerReadDto>(createdResult.Payload);

        Assert.NotNull(await service.GetCustomerByIdAsync(created.Id, companyId: 1));
        Assert.Null(await service.GetCustomerByIdAsync(created.Id, companyId: 2));

        var foreignUpdate = await service.UpdateCustomerAsync(created.Id, new CustomerWriteDto
        {
            Id = created.Id,
            Name = "Modificado"
        }, companyId: 2);
        Assert.Equal(ServiceStatus.NotFound, foreignUpdate.Status);

        var foreignDelete = await service.DeleteCustomerAsync(created.Id, companyId: 2, deletedBy: "99");
        Assert.Equal(ServiceStatus.NotFound, foreignDelete.Status);
    }
}