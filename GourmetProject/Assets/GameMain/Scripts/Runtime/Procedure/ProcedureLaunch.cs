using GameFramework.Fsm;
using GameFramework.Procedure;
using GourmetProject.Core.Diagnostics;

namespace GourmetProject.Runtime.Procedure
{
    /// <summary>
    /// 启动流程：GameFramework 就绪后第一时间初始化基础服务，然后进入预加载流程。
    /// 与玩法无关，仅负责把基础设施拉起。
    /// </summary>
    public sealed class ProcedureLaunch : ProcedureBase
    {
        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            GameApp.InitializeServices();
            Log.Info("ProcedureLaunch done.", GameApp.Tag);

            ChangeState<ProcedurePreload>(procedureOwner);
        }
    }
}
