# HSDK TGA 匿名游玩统计

## 接入与隐私

- 业务代码统一调用 `GameAnalyticsService`，不得直接调用 HSDK 的 TGA 事件接口。
- HSDK 只在玩家允许匿名数据统计后初始化；拒绝或撤回时，业务事件会被丢弃，待发送队列会被清空。
- 当前游戏没有账号系统，因此不调用 `HSDK.SetTGAAccountId`；HSDK 使用本地生成的匿名 `distinctId`。
- 当前按示例配置使用 `gameId=fkttl-test` 和 `ENV.Test`。切生产环境前必须同时确认正式 `gameId` 并切换为 `ENV.Production`。

## TGA 数据约束

- 所有业务事件只使用扁平标量属性，不使用列表、对象或对象组，因此不会产生第二层对象组。
- 当前共 12 个事件、53 个复用的业务属性名；加上 HSDK 的 31 个字段后仍低于建议的 300 个事件属性上限。
- 业务属性最多 64 个，为 HSDK 自动补充字段预留空间，保证单个 `extra` 对象不超过 100 个子属性。
- 文本按 UTF-8 截断到 2KB；数值会收敛到 `-9E15` 至 `9E15`，`NaN` 和无穷值会转为 `0`。
- 首次事件统一复用 `event_unique_id`，在客户端会话内去重，并可在分析侧跨会话去重。
- 属性值类型由第一次入库决定。已有属性不得在后续版本中改变类型。

更多限制见 [ThinkingData 数据规则](https://docs-v2.thinkingdata.cn/?version=v5.0&lan=zh-CN&code=data_format&anchorId=)。

## 公共属性

所有局内业务事件都包含：

`schema_version, app_version, build_guid, platform, channel, build_type, run_id, character_id, is_tutorial, week_index, day_value, day_bucket, archetype_id, archetype_0_share, archetype_1_share, archetype_2_share, archetype_lead`

流派是事件发生时的菜谱快照，不是永久用户属性。`archetype_id` 为 `0 / 1 / 2 / mixed`。

## 事件字典

| 事件 | 关键字段 | 说明 |
|---|---|---|
| `run_started` | `run_id, character_id, is_tutorial` | 新局创建并完成存档 |
| `run_checkpoint_reached` | `week_index, day_boundary` | 首次到达 WnDm 整数边界 |
| `run_milestone_reached` | `score_*, target_score` | 达成当前通关目标但仍可继续无尽 |
| `run_ended` | `end_reason, is_death` | `hearts_zero` 或 `replaced` |
| `choice_candidate_shown` | `offer_id, context, content_type, content_id, base_id, position, candidate_count, required_pick_count, selection_mode, revision` | 候选首次展示 |
| `choice_candidate_selected` | 与 shown 相同 | 候选成功生效后上报 |
| `choice_offer_resolved` | `offer_id, context, selected_count, skipped, reroll_count, duration_ms` | 批次完成、放弃或重抽 |
| `shop_item_shown` | `shop_id, spend_category, content_id, slot, price, affordable, restock_index` | 商品或自动补货首次展示 |
| `shop_purchase` | `shop_id, spend_category, content_id, gold_spent, gold_before, gold_after` | 成功扣除实际价格后上报 |
| `battle_started` | `battle_id, is_boss, boss_id, target_score, hearts_before` | 战斗会话建立 |
| `battle_settled` | `battle_id, is_boss, boss_id, target_hit, survived, terminal_death, hearts_after, score_*` | 扣心和免死全部结算后 |
| `active_item_used` | `item_id, use_context` | 主动道具成功消耗并生效 |

`score_*` 指 `score_mantissa, score_exponent, score_log10, score_text`。禁止将 `BigDouble` 直接强转为普通浮点分数。

选择 `context`：`daily_action, battle_reward, boss_reward, event_reward, slot_reward, shop_fragment`。

商店 `spend_category`：`dish, passive_item, active_item, fragment, dish_delete_service`。

`selection_mode=optional` 才进入选择率；一选一或全部必选为 `forced`，直接发放为 `auto`。

## 指标口径

- 可选选取率：`optional` 的 selected 次数 / shown 次数。
- 批次选取率：选中内容的去重 `offer_id` / 出现内容的去重 `offer_id`。
- 商店花费占比：分类 `gold_spent` 总和 / 全部商店 `gold_spent` 总和。
- WnDm 区间存活率：到达下一边界的去重 `run_id` / 到达当前边界的去重 `run_id`；最后一天连接下一周 D0。
- 覆盖存档属于删失，不属于死亡；尚未走完观察窗口的进行中局不计入死亡分母。
- Boss 分开计算到达率、达标率、战后存活率和终结死亡率。
- 分数按周天、流派、Boss 输出 P10/P50/P90、均值和样本量，跨量级比较使用 `score_log10`。
