namespace digital_services.Objects.Auth
{
    public class RegisterUserDto
    {
        public string Email { get; set; }
        public string Name { get; set; }
        public string Password { get; set; }
        public int AreaId { get; set; }
        public int ProfileId { get; set; }
    }
}