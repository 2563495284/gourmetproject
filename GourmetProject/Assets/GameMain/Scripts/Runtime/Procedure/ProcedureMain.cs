using GameFramework.Fsm;
using GameFramework.Procedure;
using GourmetProject.Core.Diagnostics;

namespace GourmetProject.Runtime.Procedure
{
    /// <summary>
    /// 主流程：基础框架就绪后的停靠点。玩法层接入后，可从这里切换到自己的菜单/对局流程。
    /// 现在仅作为"框架已就绪"的空闲态存在，不含任何玩法逻辑。
    /// </summary>
    public sealed class ProcedureMain : ProcedureBase
    {
        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("ProcedureMain entered. Base framework is ready for gameplay to hook in.", GameApp.Tag);
        }
    }
}
