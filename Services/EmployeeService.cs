using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories;
using InventoryAPI.Repositories.IRepositories;
using InventoryAPI.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace InventoryAPI.Services
{
    public class EmployeeService(IWorkFlow workFlow) : WorkContainer<Employee>(workFlow), IEmployeeService
    {
        private static EmployeeReadDto ToReadDto(Employee e) => new(
            e.Id,
            e.UserId,
            e.FirstName,
            e.LastName,
            e.DNI,
            e.Age,
            e.BirthDate,
            e.JobPositionId,
            e.AreaId,
            e.HireDate,
            e.ContractTypeId,
            e.StatusId,
            e.CompanyId,
            e.User?.ProfilePictureUrl ?? e.PictureUrl,
            e.IsActive,
            e.Initial);

        public async Task<List<EmployeeReadDto>> GetEmployeesByCompanyAsync(int companyId)
        {
            var activeEmployees = await _workFlow.Repository<Employee>()
                .Query(asNoTracking: true)
                .Include(e => e.User)
                .Where(e => e.CompanyId == companyId && e.IsActive)
                .ToListAsync();

            return [.. activeEmployees.Select(ToReadDto)];
        }

        public async Task<EmployeeReadDto?> GetByIdInCompanyAsync(int id, int companyId)
        {
            var emp = await _workFlow.Repository<Employee>()
                .Query(asNoTracking: true)
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId);

            return emp is null ? null : ToReadDto(emp);
        }

        public async Task<ServiceResult> CreateInCompanyAsync(EmployeeWriteDto dto, int companyId)
        {
            if (dto.UserId.HasValue && dto.UserId.Value > 0)
            {
                var userInCompany = await _workFlow.Repository<User>()
                    .Query(asNoTracking: true)
                    .AnyAsync(u => u.Id == dto.UserId.Value && u.CompanyId == companyId);
                if (!userInCompany)
                    return ServiceResult.BadRequest("El usuario vinculado no pertenece a esta empresa.");
            }

            var employee = new Employee
            {
                UserId = dto.UserId,
                FirstName = dto.FirstName?.Trim(),
                LastName = dto.LastName?.Trim(),
                DNI = dto.DNI?.Trim(),
                Age = dto.Age,
                BirthDate = dto.BirthDate?.Trim(),
                JobPositionId = dto.JobPositionId,
                AreaId = dto.AreaId,
                HireDate = dto.HireDate?.Trim(),
                ContractTypeId = dto.ContractTypeId,
                StatusId = dto.StatusId,
                CompanyId = companyId,
                IsActive = true
            };

            if (!await CreateAsync(employee))
                return ServiceResult.BadRequest("No se pudo crear el empleado.");

            return ServiceResult.Created(ToReadDto(employee));
        }

        public async Task<ServiceResult> UpdateInCompanyAsync(int id, EmployeeWriteDto dto, int companyId)
        {
            if (id != dto.Id) return ServiceResult.BadRequest("El ID no coincide.");

            var existingEmployee = await _workFlow.Repository<Employee>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId);

            if (existingEmployee is null) return ServiceResult.NotFound();

            existingEmployee.FirstName = dto.FirstName?.Trim();
            existingEmployee.LastName = dto.LastName?.Trim();
            existingEmployee.DNI = dto.DNI?.Trim();
            existingEmployee.Age = dto.Age;
            existingEmployee.BirthDate = dto.BirthDate?.Trim();
            existingEmployee.JobPositionId = dto.JobPositionId;
            existingEmployee.AreaId = dto.AreaId;
            existingEmployee.HireDate = dto.HireDate?.Trim();
            existingEmployee.ContractTypeId = dto.ContractTypeId;

            _workFlow.Repository<Employee>().Update(existingEmployee);
            await _workFlow.CompleteAsync();
            return ServiceResult.NoContent();
        }

        public async Task<ServiceResult> UpdateEmployeeWithUserInCompanyAsync(int id, EmployeeUpdateDTO dto, int companyId)
        {
            if (dto?.Employee is null || id != dto.Employee.Id)
                return ServiceResult.BadRequest("Datos inválidos o ID no coincide.");

            var employeeDb = await _workFlow.Repository<Employee>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId);

            if (employeeDb is null)
                return ServiceResult.NotFound("Empleado no encontrado en tu empresa.");

            employeeDb.FirstName = dto.Employee.FirstName?.Trim();
            employeeDb.LastName = dto.Employee.LastName?.Trim();
            employeeDb.DNI = dto.Employee.DNI?.Trim();
            employeeDb.JobPositionId = dto.Employee.JobPositionId;
            employeeDb.AreaId = dto.Employee.AreaId;

            if (employeeDb.UserId.HasValue && employeeDb.UserId > 0 && !string.IsNullOrWhiteSpace(dto.UpdatedEmail))
            {
                var userDb = await _workFlow.Repository<User>()
                    .Query(asNoTracking: false)
                    .FirstOrDefaultAsync(u => u.Id == employeeDb.UserId.Value && u.CompanyId == companyId);

                if (userDb is not null)
                {
                    userDb.Email = dto.UpdatedEmail.Trim();
                    _workFlow.Repository<User>().Update(userDb);
                }
            }

            _workFlow.Repository<Employee>().Update(employeeDb);
            await _workFlow.CompleteAsync();
            return ServiceResult.Ok(new { mensaje = "Empleado y usuario actualizados correctamente." });
        }

        public async Task<ServiceResult> DeleteInCompanyAsync(int id, int companyId, string deletedBy)
        {
            var existingEmployee = await _workFlow.Repository<Employee>()
                .Query(asNoTracking: false)
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId);

            if (existingEmployee is null) return ServiceResult.NotFound();

            existingEmployee.IsActive = false;
            if (existingEmployee.User is not null && existingEmployee.User.CompanyId == companyId)
                existingEmployee.User.IsActive = false;

            _workFlow.Repository<Employee>().Update(existingEmployee);
            await _workFlow.CompleteAsync();
            return ServiceResult.NoContent();
        }

        public async Task<(bool Success, string Message)> UpdateEmployeeWithUserAsync(int id, EmployeeUpdateDTO dto)
        {
            if (dto?.Employee is null || dto.Employee.CompanyId <= 0)
                return (false, "Empresa inválida.");

            var result = await UpdateEmployeeWithUserInCompanyAsync(id, dto, dto.Employee.CompanyId);
            return result.Status == ServiceStatus.Ok
                ? (true, "Empleado y usuario actualizados correctamente.")
                : (false, result.Error ?? "No se pudo actualizar.");
        }
    }
}