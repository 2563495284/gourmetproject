using GameFramework.Fsm;

namespace GourmetProject.Runtime.Fsm
{
    /// <summary>
    /// 有限状态机状态基类（开箱即用）。基于 GameFramework 的 FsmState，统一暴露可重写的
    /// 生命周期钩子，玩法层的状态继承本类即可。与玩法无关。
    /// 使用方式：GameApp.Fsm.CreateFsm(name, owner, stateA, stateB, ...)。
    /// </summary>
    public abstract class FsmStateBase<T> : FsmState<T> where T : class
    {
        protected override void OnInit(IFsm<T> fsm)
        {
            base.OnInit(fsm);
        }

        protected override void OnEnter(IFsm<T> fsm)
        {
            base.OnEnter(fsm);
        }

        protected override void OnUpdate(IFsm<T> fsm, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(fsm, elapseSeconds, realElapseSeconds);
        }

        protected override void OnLeave(IFsm<T> fsm, bool isShutdown)
        {
            base.OnLeave(fsm, isShutdown);
        }

        protected override void OnDestroy(IFsm<T> fsm)
        {
            base.OnDestroy(fsm);
        }
    }
}
