using System.Threading.Tasks;

namespace OcuNet;

internal interface IFileWriter
{
    Task SaveScreenshot(string path, byte[] screenshotBytes);
}
