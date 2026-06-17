using System.Drawing;
using System.Threading.Tasks;

namespace OcuNet;

internal interface IMonitorService
{
    public Task<MonitorDescription[]> GetMonitors();

    public Task<MonitorDescription> GetMonitor(int index);

    public Task<Bitmap> GetScreenshot(MonitorDescription monitor);
}
