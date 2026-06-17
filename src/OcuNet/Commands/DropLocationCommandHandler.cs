using System.Threading.Tasks;

namespace OcuNet.Commands;

internal class DropLocationCommandHandler : BaseClickLocationCommandHandler
{
    public DropLocationCommandHandler(IMouseController mouseController)
        : base(mouseController)
    {
    }

    public override Task Execute(MouseLocationCommand command) => Execute(command, this.MouseController.DropTo);
}
