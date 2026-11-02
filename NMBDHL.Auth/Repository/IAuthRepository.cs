
using NMBDHL.Auth.Model;
using System.Threading.Tasks;

namespace NMBDHL.Auth.Repository
{
    public interface IAuthRepository
    {
        Task<User> ValidateUser(Login login);

        Task<User?> FindUserByEmailAsync(string email);

        Task<bool> UpdatePasswordAsync(
            string email,
            string passwordHash);
    }
}
