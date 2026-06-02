using GameFramework.Fsm;
using GameFramework.Procedure;
using GourmetProject.Core.Diagnostics;

namespace GourmetProject.Runtime.Procedure
{
    /// <summary>
    /// 预加载流程：一次性加载全局配置（Luban 表）等与玩法无关的启动资源，完成后进入主流程。
    /// 这里只做基础设施级的预加载，具体玩法资源由玩法层在自己的流程里加载。
    /// </summary>
    public sealed class ProcedurePreload : ProcedureBase
    {
        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            // 一次性加载全部 Luban 配置表。
            GameApp.Config.LoadAll();
            Log.Info("ProcedurePreload: base assets ready.", GameApp.Tag);

            ChangeState<ProcedureMain>(procedureOwner);
        }
    }
}
