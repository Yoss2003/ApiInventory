using InventoryAPI.Models;
using InventoryAPI.Models.DTO;
using InventoryAPI.Repositories.IRepositories;

namespace InventoryAPI.Services.IServices
{
    public interface IProfileService : IWorkContainer<Profile>
    {
        Task<List<ProfileReadDto>> GetProfilesByCompanyAsync(int companyId);
        Task<ProfileReadDto?> GetProfileByIdAsync(int id, int companyId);
        Task<ProfileReadDto?> GetProfileByUsernameAsync(string username, int companyId);
        Task<ServiceResult> CreateProfileAsync(ProfileWriteDto dto, int companyId);
        Task<ServiceResult> UpdateProfileAsync(int id, ProfileWriteDto dto, int companyId);
    }
}