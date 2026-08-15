# Path of Idle 独立编辑器

这是一个“独立 WPF 桌面程序 + 无界面 BepInEx 桥接 Mod”的项目。编辑器 UI 不会嵌入游戏；桥接 Mod 只在后台通过本机命名管道接收请求，并在 Unity 主线程调用游戏原生数据接口。

## 功能

- 装备生成：按名称或 ID 搜索装备，并按游戏实时规则选择可用品级、合法装备等级。
- 装备锻造：按当前 `TEquipForge` 与最终装备等级规则选择锻造等级，生成时由原生装备初始化流程重算属性。
- 词条编辑：读取当前装备的原生词条池和数量上限，支持逐条添加、删除和修改合法等级；普通随机词条显示随等级变化的原生数值范围并默认使用当前等级最大值，手动留空仍由游戏随机；特殊随机词条不伪造范围。
- 神话词条：按具体装备的 `legendAffixArr` 读取绑定候选、原生倍率和特殊随机方式，并以独立红色分类显示。
- 物品编辑：资源、工具/宝箱、符文和奇物按子标签分类；可按名称或 ID 搜索、增加物品并修改背包堆叠数量（数量 0 表示删除）。
- 符文可选择当前 `TRuneQuality` 表中的品级；物品列表同时读取普通背包、符文背包、奇物背包和优先存放道具的 5x5 道具袋。
- 宝箱等级：装备宝箱等级及其产出装备等级范围读取自当前游戏的 `TBoxLevel` 和版本上限，添加后保留当前物品与等级选择。
- 角色编辑：名称、等级、品级、赐福等级、力量、敏捷、智力、技能树节点、技能等级和剩余技能点；列表按基础技能、天赋技能、天赋专精、异化技能和启迪天赋分组。每个职业固定的三个基础技能只读；消耗魔力的天赋技能按游戏 `type + index` 槽位池从所有职业选择，下拉项标注所属职业，同池条件分支只能二选一；天赋专精作为被动项独立显示。
- 角色品级通过游戏原生 `HeroData.ChangeQuality` 逐级调整，并由游戏重建基础属性、每级成长、异化技能和技能点；不消耗监牢材料，也不会随机升降。
- 角色成长：可直接编辑每级属性成长且不消耗材料，单项上下限与总点数由当前职业、品级的游戏规则实时校验；需要消耗血肉结晶的监牢重随入口不在编辑器中提供。
- 额外技能：异化技能上限按角色品级和建筑属性动态读取；启迪天赋上限按神殿属性动态读取，二者均显示在技能表中。
- 现有异化技能和启迪天赋均可在技能表中直接更换候选并编辑等级；技能表会以不同颜色标识普通、异化和启迪来源。启迪候选按 `masteryId` 显示实际专精名称，避免成对天赋节点名称与效果错位。
- 崇拜者：独立页面编辑崇拜者等级；职业魔偶按实际修改项分别提交，力量/敏捷/智力按当前等级累计总加成校验，累计范围由每次升级的职业独立范围逐级相加；技能/专精加成按当前职业中带 `masteryId` 的 `TTalent` 项读取并可编辑。
- 修改成功后调用当前游戏存档的原生 `SaveData()`。
- 忙于冒险的角色会拒绝修改，以减少运行时数据损坏风险。

## 运行环境

- Windows x64。
- 已安装《Path of Idle》。
- 游戏已安装并成功运行过适用于 IL2CPP 游戏的 BepInEx 6；发布包不包含 BepInEx。
- 论坛发布版 `PathOfIdleEditor.exe` 已内置 .NET 8 桌面运行库，无需另外安装 .NET。

## 项目结构

```text
PathOfIdleEditor.csproj       BepInEx 后台桥接 Mod（无 UI）
PathOfIdleEditor.App/         独立 Windows WPF 编辑器
```

## 构建

```powershell
dotnet build .\PathOfIdleEditor.csproj -c Release
dotnet build .\PathOfIdleEditor.App\PathOfIdleEditor.App.csproj -c Release
```

论坛发布版桌面程序使用 Windows x64 自包含单文件：

```powershell
dotnet publish .\PathOfIdleEditor.App\PathOfIdleEditor.App.csproj -c Release
```

发布时只需提供 `PathOfIdleEditor.App\bin\Release\net8.0-windows\win-x64\publish\PathOfIdleEditor.exe`
和桥接 Mod 的 `bin\Release\net6.0\PathOfIdleEditorBridge.dll`。桌面程序已内置 .NET 8 运行库。

桥接默认引用 `D:\app\Steam\steamapps\common\PathOfIdle`，可通过 `-p:PathOfIdleGameDir='其他目录'` 覆盖。

## 安装和使用

1. 备份游戏存档并关闭游戏。
2. 确认游戏已经安装 BepInEx 6，并且至少成功启动过一次。
3. 将发布包中的 `PathOfIdleEditorBridge.dll` 复制到游戏的 `BepInEx\plugins`。
4. 启动游戏并进入一个游戏存档，建议回到城镇。
5. 在任意位置单独启动发布包中的 `PathOfIdleEditor.exe`。
6. 点击“连接 / 刷新”，然后使用装备生成、物品编辑、崇拜者或角色编辑页面。

桌面程序只连接本机命名管道 `PathOfIdleEditor.v1`，不会监听网络端口。关闭游戏后连接会自动失效。

## 注意

- 品级、装备等级、词条池、词条数量、词条等级、技能候选和技能等级上限均在连接时从当前游戏表或运行时方法读取，并在提交时再次验证。
- 物品清单来自当前游戏的数据表；增减数量通过游戏原生背包方法完成，以保留堆叠、背包容量和刷新事件规则。
- 角色等级受当前游戏允许的最高角色等级限制。
- 游戏更新后 interop 类型可能变化，需要重新构建或适配。
