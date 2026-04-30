namespace TagForge.Services
{
    public interface ISecurityService
    {
        /// <summary>
        /// Encrypts the specified plain text using platform-appropriate protection.
        /// </summary>
        string? Encrypt(string? plainText);

        /// <summary>
        /// Decrypts the specified encrypted text using platform-appropriate protection.
        /// </summary>
        string? Decrypt(string? encryptedText);
    }
}
