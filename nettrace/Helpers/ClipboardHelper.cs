using Windows.ApplicationModel.DataTransfer;

namespace NetTrace.Helpers;

public static class ClipboardHelper
{
    public static bool TryCopy(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            return true;
        }
        catch (Exception)
        {
            // the clipboard can be locked by another process; copying is best effort
            return false;
        }
    }
}
