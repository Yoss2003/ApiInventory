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
        public override async Task<IEnumerable<Employee>> GetAllAsync()
        {
            var activeEmployees = await _workFlow.Repository<Employee>()
                .Query(asNoTracking: true)
                .Include(e => e.User)
                .Where(e => e.IsActive)
                .ToListAsync();

            foreach (var emp in activeEmployees)
            {
                if (emp.User != null) emp.PictureUrl = emp.User.ProfilePictureUrl;
            }
            return activeEmployees;
        }

        public override async Task<IEnumerable<Employee>> GetAllByCompanyIdAsync(int companyId)
        {
            var activeEmployees = await _workFlow.Repository<Employee>()
                .Query(asNoTracking: true)
                .Include(e => e.User)
                .Where(e => e.CompanyId == companyId && e.IsActive)
                .ToListAsync();

            foreach (var emp in activeEmployees)
            {
                if (emp.User != null) emp.PictureUrl = emp.User.ProfilePictureUrl;
            }
            return activeEmployees;
        }

        public async Task<Employee?> GetByIdInCompanyAsync(int id, int companyId)
        {
            var emp = await _workFlow.Repository<Employee>()
                .Query(asNoTracking: true)
                .Include(e => e.User)
                .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId);

            if (emp?.User != null) emp.PictureUrl = emp.User.ProfilePictureUrl;
            return emp;
        }

        public async Task<ServiceResult> CreateInCompanyAsync(Employee employee, int companyId)
        {
            employee.Id = 0;
            employee.CompanyId = companyId;
            employee.IsActive = true;

            if (employee.UserId.HasValue && employee.UserId.Value > 0)
            {
                var userInCompany = await _workFlow.Repository<User>()
                    .Query(asNoTracking: true)
                    .AnyAsync(u => u.Id == employee.UserId.Value && u.CompanyId == companyId);
                if (!userInCompany)
                    return ServiceResult.BadRequest("El usuario vinculado no pertenece a esta empresa.");
            }

            if (!await CreateAsync(employee))
                return ServiceResult.BadRequest("No se pudo crear el empleado.");

            return ServiceResult.Created(employee);
        }

        public async Task<ServiceResult> UpdateInCompanyAsync(int id, Employee employee, int companyId)
        {
            if (id != employee.Id) return ServiceResult.BadRequest("El ID no coincide.");

            var existingEmployee = await _workFlow.Repository<Employee>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(e => e.Id == id && e.CompanyId == companyId);

            if (existingEmployee is null) return ServiceResult.NotFound();

            existingEmployee.FirstName = employee.FirstName;
            existingEmployee.LastName = employee.LastName;
            existingEmployee.DNI = employee.DNI;
            existingEmployee.JobPositionId = employee.JobPositionId;
            existingEmployee.AreaId = employee.AreaId;

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

            employeeDb.FirstName = dto.Employee.FirstName;
            employeeDb.LastName = dto.Employee.LastName;
            employeeDb.DNI = dto.Employee.DNI;
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

        public override async Task<bool> UpdateAsync(Employee employee)
        {
            var existingEmployee = await _workFlow.Repository<Employee>()
                .Query(asNoTracking: false)
                .FirstOrDefaultAsync(e => e.Id == employee.Id && e.CompanyId == employee.CompanyId);
            if (existingEmployee == null) return false;

            existingEmployee.FirstName = employee.FirstName;
            existingEmployee.LastName = employee.LastName;
            existingEmployee.DNI = employee.DNI;
            existingEmployee.JobPositionId = employee.JobPositionId;
            existingEmployee.AreaId = employee.AreaId;

            _workFlow.Repository<Employee>().Update(existingEmployee);
            return await _workFlow.CompleteAsync() > 0;
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