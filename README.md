# Path of Idle 独立编辑器

这是一个“独立 WPF 桌面程序 + 无界面 BepInEx 桥接 Mod”的项目。编辑器 UI 不会嵌入游戏；桥接 Mod 只在后台通过本机命名管道接收请求，并在 Unity 主线程调用游戏原生数据接口。

## 功能

- 装备生成：按名称或 ID 搜索装备，并按游戏实时规则选择可用品级、合法装备等级。
- 词条编辑：读取当前装备的原生词条池和数量上限，支持逐条添加、删除和修改合法等级。
- 角色编辑：名称、等级、力量、敏捷、智力、技能树节点、技能更换、技能等级和剩余技能点。
- 修改成功后调用当前游戏存档的原生 `SaveData()`。
- 忙于冒险的角色会拒绝修改，以减少运行时数据损坏风险。

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

桥接默认引用 `D:\app\Steam\steamapps\common\PathOfIdle`，可通过 `-p:PathOfIdleGameDir='其他目录'` 覆盖。

## 安装和使用

1. 备份游戏存档并关闭游戏。
2. 将 `bin\Release\net6.0\PathOfIdleEditorBridge.dll` 复制到游戏的 `BepInEx\plugins`。
3. 启动游戏并进入一个游戏存档，建议回到城镇。
4. 单独启动 `PathOfIdleEditor.App\bin\Release\net8.0-windows\PathOfIdleEditor.exe`。
5. 点击“连接 / 刷新”，然后使用装备生成或角色编辑页。

桌面程序只连接本机命名管道 `PathOfIdleEditor.v1`，不会监听网络端口。关闭游戏后连接会自动失效。

## 注意

- 品级、装备等级、词条池、词条数量、词条等级、技能候选和技能等级上限均在连接时从当前游戏表或运行时方法读取，并在提交时再次验证。
- 角色等级受当前游戏允许的最高角色等级限制。
- 游戏更新后 interop 类型可能变化，需要重新构建或适配。
