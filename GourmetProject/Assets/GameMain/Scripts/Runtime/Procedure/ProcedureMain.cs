using System;
using GameFramework.Fsm;
using GameFramework.Procedure;
using GourmetProject.Core.Diagnostics;

namespace GourmetProject.Runtime.Procedure
{
    /// <summary>
    /// 主流程：基础框架就绪后的停靠点。若玩法层提供了菜单流程（通过类型名数据驱动配置），
    /// 则切入该流程；否则保持空闲态。基础层不直接引用玩法程序集，靠反射解析类型名以维持单向依赖。
    /// </summary>
    public sealed class ProcedureMain : ProcedureBase
    {
        /// <summary>
        /// 玩法菜单流程的程序集限定类型名。基础层不编译期引用 Game 程序集，
        /// 故用字符串 + 反射解析；解析失败则保持停靠态，不影响基础框架运行。
        /// </summary>
        private const string MenuProcedureTypeName = "GourmetProject.Game.Procedure.ProcedureMenu, GourmetProject.Game";

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);
            Log.Info("ProcedureMain entered. Base framework is ready for gameplay to hook in.", GameApp.Tag);

            Type menuType = Type.GetType(MenuProcedureTypeName);
            if (menuType != null)
            {
                Log.Info("ProcedureMain: switching to gameplay menu procedure.", GameApp.Tag);
                ChangeState(procedureOwner, menuType);
            }
        }
    }
}
