using InventoryAPI.Data;
using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories;
using InventoryAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Tests;

public sealed class EmployeeIsolationTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetByIdInCompanyAsync_ReturnsNull_WhenEmployeeBelongsToAnotherCompany()
    {
        await using var db = CreateContext();
        db.Employees.Add(new Employee
        {
            Id = 10,
            CompanyId = 1,
            FirstName = "Carlos",
            LastName = "Perez",
            DNI = "12345678",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new EmployeeService(new WorkFlow(db));

        var result = await service.GetByIdInCompanyAsync(10, companyId: 2);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateInCompanyAsync_ReturnsNotFound_AndDoesNotModify_AnotherCompanyEmployee()
    {
        await using var db = CreateContext();
        db.Employees.Add(new Employee
        {
            Id = 20,
            CompanyId = 1,
            FirstName = "Ana",
            LastName = "Lopez",
            DNI = "87654321",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new EmployeeService(new WorkFlow(db));
        var tampered = new EmployeeWriteDto
        {
            FirstName = "Hackeado",
            LastName = "Externo",
            DNI = "00000000"
        };

        var result = await service.UpdateInCompanyAsync(20, tampered, companyId: 2);

        Assert.Equal(ServiceStatus.NotFound, result.Status);

        var stored = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == 20);
        Assert.Equal("Ana", stored.FirstName);
        Assert.Equal(1, stored.CompanyId);
    }

    [Fact]
    public async Task DeleteInCompanyAsync_ReturnsNotFound_WhenEmployeeBelongsToAnotherCompany()
    {
        await using var db = CreateContext();
        db.Employees.Add(new Employee
        {
            Id = 30,
            CompanyId = 1,
            FirstName = "Luis",
            LastName = "Diaz",
            DNI = "44556677",
            IsActive = true
        });
        await db.SaveChangesAsync();

        var service = new EmployeeService(new WorkFlow(db));

        var result = await service.DeleteInCompanyAsync(30, companyId: 2);

        Assert.Equal(ServiceStatus.NotFound, result.Status);

        var stored = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == 30);
        Assert.True(stored.IsActive);
    }
}