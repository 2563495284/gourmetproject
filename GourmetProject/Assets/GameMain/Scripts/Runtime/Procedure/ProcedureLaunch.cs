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
        private IFsm<IProcedureManager> _procedureOwner;

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            GameApp.InitializeServices();
            _procedureOwner = procedureOwner;

            if (GameApp.Base.EditorResourceMode)
            {
                Log.Info("ProcedureLaunch: editor resources are ready.", GameApp.Tag);
                ContinueToPreload();
                return;
            }

            Log.Info("ProcedureLaunch: initializing packaged resources.", GameApp.Tag);
            GameApp.Resource.InitResources(OnInitResourcesComplete);
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            _procedureOwner = null;
            base.OnLeave(procedureOwner, isShutdown);
        }

        private void OnInitResourcesComplete()
        {
            Log.Info(
                $"ProcedureLaunch: packaged resources ready ({GameApp.Resource.ApplicableGameVersion}, internal {GameApp.Resource.InternalResourceVersion}).",
                GameApp.Tag);
            ContinueToPreload();
        }

        private void ContinueToPreload()
        {
            IFsm<IProcedureManager> procedureOwner = _procedureOwner;
            if (procedureOwner == null)
            {
                return;
            }

            ChangeState<ProcedurePreload>(procedureOwner);
        }
    }
}
