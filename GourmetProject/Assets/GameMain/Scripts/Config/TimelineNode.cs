// 运行时行动轴节点模型。配置节点内嵌于 Timeline，运行时快照转换为此类型供 UI/结算使用。

using Luban;
using Luban.SimpleJSON;


namespace cfg
{
public sealed partial class TimelineNode : Luban.BeanBase
{
    public TimelineNode(JSONNode _buf) 
    {
        { if(!_buf["id"].IsString) { throw new SerializationException(); }  Id = _buf["id"]; }
        { if(!_buf["timelineId"].IsString) { throw new SerializationException(); }  TimelineId = _buf["timelineId"]; }
        { if(!_buf["day"].IsNumber) { throw new SerializationException(); }  Day = _buf["day"]; }
        { if(!_buf["actionId"].IsString) { throw new SerializationException(); }  ActionId = _buf["actionId"]; }
    }

    public static TimelineNode DeserializeTimelineNode(JSONNode _buf)
    {
        return new TimelineNode(_buf);
    }

    /// <summary>
    /// 节点 id
    /// </summary>
    public readonly string Id;
    /// <summary>
    /// 所属行动轴模板 id
    /// </summary>
    public readonly string TimelineId;
    /// <summary>
    /// 整天位置
    /// </summary>
    public readonly int Day;
    /// <summary>
    /// 引用的原子行动 id（对应 action.id）
    /// </summary>
    public readonly string ActionId;
   
    public const int __ID__ = 1630749187;
    public override int GetTypeId() => __ID__;

    public  void ResolveRef(Tables tables)
    {
    }

    public override string ToString()
    {
        return "{ "
        + "id:" + Id + ","
        + "timelineId:" + TimelineId + ","
        + "day:" + Day + ","
        + "actionId:" + ActionId + ","
        + "}";
    }
}
}
