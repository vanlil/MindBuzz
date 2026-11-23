namespace MindBuzz.App.Platform;

/// <summary>
/// The only door to per-OS behaviour that leaves the app. Nothing above this interface knows which platform
/// it is on. A guard test pins the member list, so a new capability cannot creep in unnoticed.
/// </summary>
public interface IPlatformShell
{
    /// <summary>
    /// Hands a web or mail address to the OS, which gives it to the browser or mail program the user chose.
    /// Implementations must refuse anything <see cref="SafeUrl.IsAllowed"/> refuses, so no other scheme
    /// (<c>file:</c>, <c>javascript:</c> and friends) can arrive this way.
    /// </summary>
    /// <returns>True when the address was handed over; false when it was refused or the OS had no handler.</returns>
    bool OpenUrl(string url);

    /// <summary>
    /// Writes <paramref name="pdf"/> to a new folder in the system's temp folder as <paramref name="fileName"/> and
    /// opens it in the PDF viewer the user chose. The folder is deleted when MindBuzz quits.
    /// </summary>
    /// <returns>True when the file was handed over; false when it could not be written or the OS had no
    /// handler.</returns>
    bool OpenTemporaryPdf(byte[] pdf, string fileName);
}
