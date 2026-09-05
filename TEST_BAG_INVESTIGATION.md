# 背包掉落机制诊断测试

## 目标
找出为什么有 2 次 `itemTypes: (empty)` 但只有 1 个背包

## 测试方法
1. 启动游戏
2. 按 F8 进行多次拾荒（建议 10-15 次）
3. 关闭游戏
4. 检查最新日志

## 关键改进
在 Mod.cs 中新增了 `element` 字段读取：
- 尝试从 `GameItem.element.id` 获取物品ID
- 尝试从字典中查找对应价值
- 这应该能识别出背包的真实ID和价值（150）

## 预期结果
日志中应该显示：
- `element.id: <真实物品ID>`
- `element.baseValue (from dictionary): <价值>`

对于背包，应该看到：
- `element.id: bag_small` 或类似ID
- `element.baseValue: 150`

对于其他 `(empty)` 物品，价值应该不是 150

## 配置
当前 ItemTypeMultipliers: `MODULE:1.5,TOOL:1.2,MATERIAL:1.5,ALCOHOL:0.3,MEDICAL:0.8`

## 下一步
根据日志结果：
1. 确认背包的真实物品ID
2. 确认其他 `(empty)` 物品是什么
3. 更新 LOOT_ITEMS_DATABASE.md 的背包位置说明
