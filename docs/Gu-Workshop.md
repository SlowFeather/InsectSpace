# 蛊虫养炼配置

养炼配置的唯一来源是 `design/luban/GuWorkshop/Tables/`。内容组改 JSON 和来源说明，运行生成脚本；不要编辑 `Generated/` 或 `Resources/LocalGuWorkshop/` 下的 C#、bytes 文件。

## 表关系

| 文件 | 作用 | 关键字段 |
| --- | --- | --- |
| `items.json` | 食料、炼材 | `id`, `name`, `note` |
| `offers.json` | NPC 坊市商品 | `id`, `gu` 或 `item`, `quantity`, `yuanShi` |
| `care.json` | 每种蛊的食物、饱食和单炼 | `id`, `food`, `foodCount`, `fedSeconds`, `refineFee`, `success`, `destroy` |
| `recipes.json` | 合炼蛊方 | `id`, `ingredients`, `output`, `minimumRank`, `materials`, `yuanShi`, `success`, `destroy`, `source` |

`care.id` 和 `recipes.output` 指向已有 `design/luban/GuPaths/Tables/gu.json` 的蛊虫 ID。`ingredients` 是蛊定义 ID 与数量的列表，`materials` 是物品 ID 与数量的列表；列表长度和数量由校验器限制，因此一张配方可以扩展到多种材料，不需要修改协议字段。每条蛊实例仍由服务器分配独立实例 ID。

概率使用万分比：`success + destroy <= 10000`，未成功且未毁蛊的部分为“失败保留”。例如 `success=6000,destroy=1500` 表示成功 60%、失败保留 25%、毁蛊 15%。概率、饱食秒数、价格是玩法参数，不从参考文章推断。

商店行只能是“一个已配置蛊虫”或“一个已配置物品堆”：蛊虫行使用 `gu`、`item=0`、`quantity=1`；物品行使用 `gu=0` 和已配置的 `item`。NPC、价格和正式数值可继续增加行，不需要客户端代码改动。

## 示例结构

```json
{
  "id": 9001,
  "name": "示例合炼（待确认）",
  "ingredients": [{"id": 1001, "count": 1}, {"id": 1002, "count": 2}],
  "output": 2001,
  "minimumRank": 2,
  "materials": [{"id": 30001, "count": 1}, {"id": 30002, "count": 2}],
  "yuanShi": 10,
  "success": 6000,
  "destroy": 1500,
  "source": "内部设计稿 v0.1；用户确认后启用"
}
```

示例只说明字段形状。当前仓库已放入一组标注为“内部原型默认值”的首批行，供本地 Unity/TCP 联调：月兰花瓣、月华石、月光蛊、小光蛊和月芒蛊路线。它们不是最终平衡值，后续策划可直接修改四张 JSON 表并重新导表。

## 检查与生成

```powershell
./tools/Test-WorkshopTables.ps1 -AllowUnconfigured # 只检查空 schema
./tools/Test-WorkshopTables.ps1                  # 可玩配置必须全部非空
./tools/Test-WorkshopTableValidation.ps1          # 10 个合成/错误输入校验 fixture
./tools/Build-WorkshopTables.ps1                  # Luban 4.5.0 生成客户端/服务端
```

生成脚本会检查四张表、已有蛊目录引用、重复 ID、数量、排名、概率，并比较客户端与服务器 bytes 哈希。运行时如果四表全空会明确阻止启动；不会静默切换到旧的“全目录赠送”演示。

## 独立 Unity 通讯验收

停止 Unity Play 后运行 `./tools/Test-WorkshopDemo.ps1`。脚本在 `.artifacts/validation/workshop-live/<时间>/` 生成明确标记为 TEST ONLY 的临时 JSON 和 Luban 数据，启动独立 .NET 测试宿主，再通过 Unity CLI 创建测试程序集中的通讯探针。结束时退出 Play、关闭自己创建的宿主，并核对正式 JSON 与客户端/服务端 bytes 的哈希未变。

验收使用真实 Unity Bootstrap、GF TCP/KCP 和现有养炼客户端，覆盖购买、独立实例、喂养、三种单炼/合炼结果、组合、重连和战斗期间拒绝养炼。测试宿主的 stdin 可设置业务时钟和随机结果，入口仅编译进 .NET 测试程序，不存在于正式服务器。超时用客户端请求计时器模拟，在确认服务端实际提交后重连并重试，检查无二次扣费或随机抽取。

该验收验证通讯和规则执行，不代表玩家已经确认正式概率、喂养周期或配方，也不替代实际界面点击、正式表和真机验收。测试结束保持原场景，重新点 Play 可回到正常预览。

## 运行边界

服务器按业务时钟计算饥饿，缺粮进入休眠，补喂恢复；购买、喂养、单炼、合炼和装备由玩家确认。概率抽取、钱包、材料、实例和装备由服务端一次事务提交；重复操作号重放原回执。当前数据存于内存，重启服务器清空。真实账号、数据库、生产支付和微信真机传输不在本地联调范围内。
