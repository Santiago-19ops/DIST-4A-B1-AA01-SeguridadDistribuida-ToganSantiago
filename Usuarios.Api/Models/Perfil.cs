
using System.ComponentModel.DataAnnotations;

namespace Usuarios.Api.Models
{
    public class Perfil
    {
        [Key]
        public int IdPerfil { get; set; }

        [Required]
        public string UsuarioId { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Correo { get; set; } = string.Empty;
    }
}
