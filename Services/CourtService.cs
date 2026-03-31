using SmashCourt_BE.Common;
using SmashCourt_BE.DTOs.Court;
using SmashCourt_BE.Models.Entities;
using SmashCourt_BE.Models.Enums;
using SmashCourt_BE.Services.IService;
using SmashCourt_BE.Repositories.IRepository;
using SmashCourt_BE.Repositories.Interfaces;
namespace SmashCourt_BE.Services
{
    public class CourtService : ICourtService
    {
        private readonly ICourtRepository _repo;
        private readonly IBranchRepository _branchRepo;
        private readonly IUserBranchRepository _userBranchRepo;
        private readonly ICourtTypeRepository _courtTypeRepo;

        public CourtService(
            ICourtRepository repo,
            IBranchRepository branchRepo,
            IUserBranchRepository userBranchRepo,
            ICourtTypeRepository courtTypeRepo)
        {
            _repo = repo;
            _branchRepo = branchRepo;
            _userBranchRepo = userBranchRepo;
            _courtTypeRepo = courtTypeRepo;
        }

        public async Task<List<CourtDto>> GetAllByBranchAsync(
            Guid branchId, bool isStaffOrAbove)
        {
            var branch = await _branchRepo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            var courts = await _repo.GetAllByBranchAsync(branchId, isStaffOrAbove);
            return courts.Select(MapToDto).ToList();
        }

        public async Task<CourtDto> GetByIdAsync(Guid id, Guid branchId, bool isStaffOrAbove)
        {
            var branch = await _branchRepo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            var court = await _repo.GetByIdAsync(id, branchId);
            if (court == null)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            // CUSTOMER không thấy sân SUSPENDED
            if (!isStaffOrAbove && court.Status == CourtStatus.SUSPENDED)
                throw new AppException(404, "Không tìm thấy sân", ErrorCodes.NotFound);

            return MapToDto(court);
        }


        private async Task<Branch> ValidateBranchAccessAsync(
            Guid branchId, Guid currentUserId, string currentUserRole)
        {
            var branch = await _branchRepo.GetByIdAsync(branchId);
            if (branch == null)
                throw new AppException(404, "Không tìm thấy chi nhánh", ErrorCodes.NotFound);

            if (currentUserRole == UserRole.BRANCH_MANAGER.ToString())
            {
                var isInBranch = await _userBranchRepo.IsUserInBranchAsync(
                    currentUserId, branchId);
                if (!isInBranch)
                    throw new AppException(403,
                        "Bạn không có quyền thao tác chi nhánh này",
                        ErrorCodes.Forbidden);
            }

            return branch;
        }

        private static CourtDto MapToDto(Court c) => new()
        {
            Id = c.Id,
            BranchId = c.BranchId,
            CourtTypeId = c.CourtTypeId,
            CourtTypeName = c.CourtType.Name,
            Name = c.Name,
            Description = c.Description,
            AvatarUrl = c.AvatarUrl,
            Status = c.Status,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        };
    }
}
