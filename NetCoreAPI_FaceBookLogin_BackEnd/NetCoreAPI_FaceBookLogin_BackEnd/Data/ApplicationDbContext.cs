using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using NetCoreAPI_FaceBookLogin_BackEnd.Model;

namespace NetCoreAPI_FaceBookLogin_BackEnd.Data
{
    public class ApplicationDbContext : IdentityDbContext<AppUser>
    {
        public ApplicationDbContext(DbContextOptions options)
       : base(options)
        {
        }

        public DbSet<Customer> Customers { get; set; }
    }
}
