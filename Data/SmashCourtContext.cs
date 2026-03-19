using Microsoft.EntityFrameworkCore;
namespace SmashCourt_BE.Data
{
    public class SmashCourtContext : DbContext
    {
        public SmashCourtContext(DbContextOptions<SmashCourtContext> option) : base(option) {}


    }
}
