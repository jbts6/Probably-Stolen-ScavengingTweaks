# Probably Stolen - 拾荒物品完整数据库

**生成时间**：2026-09-03  
**游戏版本**：0.46D  
**数据来源**：ScavengingTweaks Mod 诊断日志

---

## 概览

拾荒系统使用**两层抽奖机制**：
1. **外层**：从TableGroup中选择一个LootTable（9个表）
2. **内层**：从选中的Table中抽取物品

共9个LootTable，包含52个常规物品 + 1个隐藏物品（**挎包，价值150**）。

---

## junkTable（垃圾表）

**外层权重**：523 (65.7%)  
**物品数量**：6

| # | 物品ID | 中文名 | 价值 |
|---|--------|--------|------|
| 1 | junk | 垃圾 | 3 |
| 2 | glass_shard | 玻璃碎片 | 2 |
| 3 | system_module_ruined | 损坏的系统模块 | 30 |
| 4 | pipe_weapon | 管道武器 | 15 |
| 5 | empty_beer_bottle | 空啤酒瓶 | 5 |
| 6 | scratcher | 刮刮乐 | 20 |

---

## materialTable（材料表）

**外层权重**：122 (15.3%)  
**物品数量**：8  
**物品类型**：MATERIAL

| # | 物品ID | 中文名 | 价值 |
|---|--------|--------|------|
| 1 | scrap_metal | 废金属 | 10 |
| 2 | nuts_metal | 金属螺母 | 14 |
| 3 | flux_agent | 助焊剂 | 4 |
| 4 | printer_plastic | 打印塑料 | 12 |
| 5 | wire | 电线 | 16 |
| 6 | gun_part | 枪械零件 | 25 |
| 7 | common_electronic | 普通电子元件 | 33 |
| 8 | kotton_fabric | 棉织物 | 40 |

---

## t1moduleTable（T1模块表）

**外层权重**：8 (1.0%)  
**物品数量**：3  
**物品类型**：MODULE  
**最高价值**：100

| # | 物品ID | 中文名 | 价值 |
|---|--------|--------|------|
| 1 | random_performance_module | 随机性能模块 | 100 |
| 2 | random_efficiency_module | 随机效率模块 | 100 |
| 3 | random_quality_module | 随机质量模块 | 100 |

**注意**：这些是占位符ID，运行时替换为真实模块。

---

## t2moduleTable（T2模块表）

**外层权重**：4 (0.5%)  
**物品数量**：3  
**物品类型**：MODULE  
**最高价值**：100

| # | 物品ID | 中文名 | 价值 |
|---|--------|--------|------|
| 1 | system_module_performance | 性能模块 | 70 |
| 2 | system_module_eco | 经济模块 | 100 |
| 3 | system_module_fineness | 精密模块 | 100 |

---

## toolTable（工具表）

**外层权重**：16 (2.0%)  
**物品数量**：5  
**物品类型**：TOOL  
**最高价值**：100

| # | 物品ID | 中文名 | 价值 |
|---|--------|--------|------|
| 1 | screwdriver | 螺丝刀 | 25 |
| 2 | wire_cutter | 剪线钳 | 40 |
| 3 | module_extractor | 模块提取器 | 100 |
| 4 | welder | 焊接器 | 80 |
| 5 | turbo_booster | 涡轮增压器 | 35 |

---

## householdTable（家居用品表）

**外层权重**：38 (4.8%)  
**物品数量**：8  
**物品类型**：SUBSTANCE, HOUSEHOLD_GOOD

| # | 物品ID | 中文名 | 价值 |
|---|--------|--------|------|
| 1 | caffeine_pill | 咖啡因药丸 | 10 |
| 2 | toothpaste | 牙膏 | 15 |
| 3 | water_filter | 净水器 | 25 |
| 4 | shampoo | 洗发水 | 30 |
| 5 | paper_towel | 厨房纸巾 | 35 |
| 6 | toilet_paper | 卫生纸 | 25 |
| 7 | full_water_test_strip_box | 水质测试条（整盒） | 0 |
| 8 | full_water_purification_tablet_box | 净水片（整盒） | 0 |

---

## makeshiftWeaponTable（临时武器表）

**外层权重**：16 (2.0%)  
**物品数量**：4  
**物品类型**：WEAPON

| # | 物品ID | 中文名 | 价值 |
|---|--------|--------|------|
| 1 | glass_shard_shiv | 玻璃碎片刀 | 8 |
| 2 | box_cutter | 美工刀 | 28 |
| 3 | kitchen_knife | 菜刀 | 35 |
| 4 | pipe_weapon | 管道武器 | 15 |

---

## packedFoodTable（包装食品表）

**外层权重**：38 (4.8%)  
**物品数量**：12  
**物品类型**：FOOD, PROCESSED_FOOD, ALCOHOL  
**最高价值**：85

| # | 物品ID | 中文名 | 价值 |
|---|--------|--------|------|
| 1 | galaxy_blend | 银河混合咖啡 | 25 |
| 2 | energy_drink | 能量饮料 | 18 |
| 3 | soda_red | 红色汽水 | 15 |
| 4 | cat_bar | 猫条能量棒 | 15 |
| 5 | li_eat_snackbar | 力食能量棒 | 17 |
| 6 | red_beer | 红啤酒 | 18 |
| 7 | processed_milk | 加工牛奶 | 16 |
| 8 | processed_juice | 加工果汁 | 14 |
| 9 | processed_meat | 加工肉制品 | 25 |
| 10 | zerochew | 零卡口香糖 | 8 |
| 11 | cup_noodle | 杯面 | 20 |
| 12 | nudka | 伏特加 | 85 |

**注意**：red_beer和nudka有ALCOHOL标签。

---

## dumpingGroundMedical（医疗用品表）

**外层权重**：40 (5.0%)  
**物品数量**：3  
**物品类型**：MEDICAL

| # | 物品ID | 中文名 | 价值 |
|---|--------|--------|------|
| 1 | bandage_item | 绷带 | 8 |
| 2 | hemostatic_bandage_item | 止血绷带 | 16 |
| 3 | topical_bandage_item | 外用绷带 | 16 |

---

## 价值分布统计

**TOP 10 最高价值物品**：
1. **挎包(小)（隐藏）- 150** ⭐
2. module_extractor（模块提取器）- 100
3. random_*_module（T1随机模块）- 100
4. system_module_eco（系统经济模块）- 100
5. system_module_fineness（系统精密模块）- 100
6. nudka（伏特加）- 85
7. welder（焊接器）- 80
8. system_module_performance（系统性能模块）- 70
9. kotton_fabric（棉织物）- 40
10. wire_cutter（剪线钳）- 40

**BOTTOM 5 最低价值物品**：
1. glass_shard（玻璃碎片）- 2
2. junk（垃圾）- 3
3. flux_agent（助焊剂）- 4
4. empty_beer_bottle（空啤酒瓶）- 5
5. bandage_item（绷带）- 8

---

## ScavengingTweaks Mod 类型倍率配置参考

当前配置：`MODULE:3.0,TOOL:2.0,ALCOHOL:0.3`

**可用类型标签**：
- `MODULE` - 模块类（t1moduleTable, t2moduleTable）
- `TOOL` - 工具类（toolTable）
- `ALCOHOL` - 酒精类（packedFoodTable的部分物品）
- `MATERIAL` - 材料类（materialTable）
- `FOOD` - 食品类（packedFoodTable）
- `WEAPON` - 武器类（makeshiftWeaponTable）
- `MEDICAL` - 医疗类（dumpingGroundMedical）
- `SUBSTANCE` - 物质类（householdTable）

**推荐配置示例**：
```
# 提升高价值工具和模块，降低酒精
MODULE:3.0,TOOL:2.0,ALCOHOL:0.3

# 提升材料，降低垃圾武器
MATERIAL:2.0,WEAPON:0.5

# 平衡食品和医疗
FOOD:1.5,MEDICAL:1.5
```

---

## 特殊说明

### `itemTypes: (empty)` 物品机制 ✓ 已确认

**测试日期**：2026-09-03  
**游戏版本**：0.46D

**结论**：✅ **并非所有 `itemTypes: (empty)` 的物品都是背包**

**已知的 `itemTypes: (empty)` 物品**：
1. **背包（bag_small）** - 价值 150，identifier 未确认
2. **捕鼠夹（mouse_trap）** - 价值 50

**识别方法**：
- 通过 `identifier` 属性区分（对应 `_identifier_k__BackingField`）
- 通过 `unitValue` 属性区分（背包=150，捕鼠夹=50）

**测试证据**：

**测试1（20:09日志）**：
- sequence=1: ALCOHOL + mouse_trap（捕鼠夹，价值50）
- **这是第一次确认捕鼠夹也是 `itemTypes: (empty)` 的物品**

**测试2（19:46日志）**：
- 2次 drops=2，其中1次确认是背包（价值150）

**背包特性**：
- **identifier**：未确认（需要等待下次获得背包时记录）
- **价值**：150（游戏中价值最高的拾荒物品）
- **itemTypes**：(empty) - 无类型标签
- **掉落机制**：作为额外奖励与任意Table的物品一起掉落（drops=2）
- **触发条件**：未知（可能是固定概率，或与拾荒次数/价值相关）

**捕鼠夹特性**：
- **identifier**：mouse_trap
- **中文名**：捕鼠夹
- **价值**：50
- **itemTypes**：(empty) - 无类型标签
- **掉落机制**：与背包相同，drops=2
- **描述**：在里面放些奶酪应该会更好用。

**如何区分背包和捕鼠夹**：
- 最简单：查看 `unitValue`，150=背包，50=捕鼠夹
- 或者：查看 `identifier`，mouse_trap=捕鼠夹
- 或者：查看 `name`，"捕鼠夹" vs "挎包(小)"

**为什么之前误以为只有背包**：
1. 捕鼠夹和背包都是 `itemTypes: (empty)`
2. 之前测试时恰好只抽到了背包，没有抽到捕鼠夹
3. 直到今天的测试才发现捕鼠夹的存在

### 占位符物品
- `random_*_module` - 运行时替换为真实模块ID
- 价值为0的物品（water test strip box等）- 可能是整盒物品，使用时展开

---

**文档维护**：如游戏版本更新导致物品列表变化，重新运行ScavengingTweaks诊断并更新本文档。
