# 奖励调整 Review

## Findings

### 主动道具不再配置独立隐藏分

当前 `RewardPoolService.RollItemChoices` 对主动道具仍然跳过严格隐藏分筛选：

```text
BuildItemCandidates(... strictHidden: !activeItem ...)
```

并且 `GetItemWeight` 只在 `item.IsPassive` 时按隐藏分调整权重。`ActiveItemGrant` 不再有独立的 `ActiveItem` 隐藏分用途；主动道具入口共用道具隐藏分上下文，但主动道具本身不按隐藏分区间筛选。

影响：主动道具奖励的 `normalHiddenOffset/hardHiddenOffset` 不再表达“主动道具隐藏分”，只保留奖励槽统一修正语义。

建议：如果后续重新给主动道具做进度分层，再新增明确字段和曲线用途。

## 变更总结

- 奖励包结构改为“固定奖励列表 + 特定奖励”。
- `baseDishSlotGroupId` 已改为 `list,string`，用于配置不可预览的固定奖励组。
- `specificSlotGroupId` 保留为行动预览对应的特定奖励组。
- `bonusSlotGroupId` 已从运行时逻辑中移除，当前代码不再读取它。
- `RewardOffer` 增加 `RewardChoiceGroup` 结构，支持多个固定奖励组和每组多选。
- `RewardForm` 改为先展示所有固定奖励组，再展示特定奖励。
- 主动道具槽支持 `choiceCount=5`、`requiredPickCount=2` 的五选二。
- 金币奖励槽不再使用基础金币倍率，改为按金币隐藏分曲线和当前槽隐藏分修正随机。
- `reward_pool.qualityWeights` 已从表、生成物和奖励池逻辑中移除。

## 表结构变化

### reward_package

保留/新增：

```text
id
baseDishSlotGroupId  list,string
specificSlotGroupId  string
```

移除/废弃：

```text
goldMin
goldMax
mainSlotGroupId
extraSlotGroupId
extraChance
bonusSlotGroupId
```

### reward_slot

保留/新增：

```text
requiredPickCount
normalHiddenOffset
hardHiddenOffset
```

移除：

```text
fallbackGold
goldMultiplierMin
goldMultiplierMax
hiddenOffset
```

### reward_pool

移除：

```text
qualityWeights
```

## 当前验证

- `bash GameConfig/gen.sh` 已成功跑通。
- `ReadLints` 当前未发现相关脚本诊断错误。
- `tbrewardpool.json` JSON 校验通过。
- `git diff --check` 通过。

## 测试缺口

- Unity MCP 当前仍连接超时，奖励相关 EditMode 测试未实际跑通。
- 建议补/跑以下测试：
  - 普通美食：基础金币 + 基础菜品 + 特定奖励。
  - 困难美食：特定奖励使用 `hardHiddenOffset`。
  - Boss：固定奖励列表包含金币/菜品/被动/格子时全部展示并可领取。
  - 主动道具五选二：领取两个不同主动道具后该组才完成。
  - 主动道具隐藏分：验证 `normalHiddenOffset/hardHiddenOffset` 会影响主动道具候选或权重。
