# GuWorkshop 表目录

这里是养炼玩法的 Luban 输入目录。JSON 文件是唯一配置来源，生成后的 C# 和 `.bytes` 文件不要手改。

## 文件

- `items.json`：食料和炼材。字段：`id`、`name`、`note`。
- `offers.json`：NPC 商店行。卖蛊时填写 `gu`、`item=0`、`quantity=1`；卖物品时填写 `gu=0`、已存在的 `item` 和数量。
- `care.json`：每种蛊的食物、饱食时长和单炼规则。`id` 必须存在于 `GuPaths/Tables/gu.json`。
- `recipes.json`：合炼蛊方。`ingredients` 和 `materials` 都是 `{id,count}` 列表，允许后续增加任意数量的材料种类。

概率使用万分比，`success + destroy <= 10000`；剩余部分是失败保留。价格、概率和饱食周期属于游戏设计参数，不能从参考资料直接推导。

## 工作流

1. 先在四张 JSON 表中补齐引用关系。
2. 运行 `../../../../tools/Test-WorkshopTables.ps1`。
3. 运行 `../../../../tools/Build-WorkshopTables.ps1`，让 Luban 同时生成客户端和服务端。
4. 运行 `../../../../tools/Test-WorkshopTableValidation.ps1` 验证错误输入不会被接受。

当前表已启用一组内部原型默认值，便于 Unity 6 本地联调；这些价格、概率、饱食周期和路线不是从参考资料推断的平衡结论，后续由策划直接修改 JSON 后重新导表。正式运营数值仍需另行评审。
