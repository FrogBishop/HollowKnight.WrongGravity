# WrongGravity 开发记录

版本：1.0.0.8  
目标游戏：Hollow Knight 1.5.78.11833  
目标框架：.NET Framework 4.7.2  
当前构建：0 warnings / 0 errors（Debug 与 Release 均已验证）

---

## 1. 这个 mod 到底改了什么

### 1.1 不是旋转物理世界

这个 mod **没有**修改 `Physics2D.gravity`，也没有旋转场景。它利用一个更小的改动面：

1. 把 Knight 的 `Rigidbody2D.gravityScale` 保持为负值。
   Unity 的默认重力是 `(0, -9.81)`；负的 `gravityScale` 会让加速度变成向上。
2. 在每次调用 `HeroController` 方法之前，把 `Rigidbody2D.velocity.y` 取反；
   调用结束后再取反回来。
   这样原版代码仍然以为“+Y 是上、-Y 是下”，它会按原逻辑计算跳跃、
   重力、冲刺、反弹等，而真实世界中的速度方向是反的。
3. 把 Knight 的 `localScale.y` 取反。
   相机投影翻转后，画面整体上下颠倒；Knight 的 sprite 和 collider 也需要
   一起翻，才能在画面上看起来是正的。
4. 把相机投影矩阵乘上 `Matrix4x4.Scale(1, -1, 1)`。
   世界中的“上”在屏幕上显示为“下”，玩家看到的重力仍然是向下的，
   但关卡是上下颠倒的。

### 1.2 为什么需要镜像射线

原版大量代码直接写死：

```csharp
Physics2D.Raycast(origin, Vector2.down, ...)
```

在反转重力下，Knight 站立的“地面”在世界坐标的上方。只翻转速度不够，
因为以下逻辑仍然会按世界下方找地面：

- `HeroController.CheckTouchingGround`
- `HeroController.CheckNearRoof`
- `HeroController.CheckForBump`
- `HeroController.CheckStillTouchingWall`（只有 checkTop 相关）
- `HeroController.CheckTouching` / `CheckTouchingAdvanced`
- `HeroController.FindGroundPoint` / `FindGroundPointY`
- PlayMaker 的 `CheckCollisionSide` / `CheckCollisionSideEnter` / `RayCast2d`

因此这些方法在 mod 中被重写或包装。重写方法都带有详细注释。

### 1.3 镜像上下文的不变量

`MirrorContext` 是一个静态嵌套深度：

- `IsActive == true` 表示当前已经在一次镜像调用内部。
- 外层 `Update` 进入镜像后，内部的 `Move` / `Jump` / `Dash` 等
  wrapper 看到 `IsActive` 为 true，就**不再**翻转速度，只调用原版。
- 每个 wrapper 使用 `BeginMirror` / `EndMirror`，并在 `finally` 中结束，
  避免原版抛异常时速度永久反向。
- `Update` / `FixedUpdate` 开头调用 `MirrorContext.Reset()`，清除可能泄漏的
  嵌套深度。

**注意**：`Reset()` 只清深度，不会恢复已经反向的 velocity。如果异常从
一个没有外层 `try/finally` 的入口逃出，速度可能保持反向。当前所有
Hero 入口都有 `try/finally`；如果以后新增入口，必须遵守同样模式。

---

## 2. 文件结构（实际代码）

```text
HollowKnight.WrongGravity/
  WrongGravity.cs                 入口、Hook 注册、相机、Hero wrapper、碰撞/地面重写
  WrongGravity.Ground.cs          GroundProbe + FindGroundPoint/Y（入口走向下搜索，其余向上优先/向下回退）
  WrongGravity.Interactions.cs     交互/门型 transition 触发器修正 + JSON profile
  WrongGravity.Mirror.cs          MirrorContext / MirrorEntity
  WrongGravity.FsmMirror.cs       CheckCollisionSide / CheckCollisionSideEnter / RayCast2d
  WrongGravity.FsmVelocity.cs     SetVelocity2d / GetVelocity2d / SetGravity2dScale
  WrongGravity.EnemyRecoil.cs     敌人垂直击退方向交换（RecoilByDirection）
  WrongGravity.Diagnostics.cs     门、顶部 kill plane、Crystal Dash FSM 补丁
  WrongGravity.Settings.cs        配置 + IMenuMod
  WrongGravity.Data.cs            DoorProfile / InteractionProfile / MirrorData / MiniJson
  Data/WrongGravityData.json      嵌入式门/交互覆盖
  README.md                       用户向说明
  DEVELOPMENT.md                  本文件
  References/                     编译引用（游戏 DLL、MMHOOK、Vasi、PlayMaker、UnityEngine）
  WrongGravity.csproj             net472 工程 + Release 打包
  .github/workflows/dotnet.yml    tag 触发的 Release CI
```

---

## 3. 每个文件的职责

### 3.1 `WrongGravity.cs`

主入口，包含：

- `Initialize`：加载嵌入 JSON，注册所有 Hook。若 `Config.Enabled == false`，
  直接返回，不安装任何 Hook。
- `GetVersion`：通过 Modding API 的 `VersionUtil` 获取版本。
- 相机：
  - `UpdateCameraMatrix`：`tk2dCamera` 重建投影后，对主相机执行
    `projectionMatrix *= Scale(1,-1,1)`。这是主相机的唯一翻转点。
  - `StartScene`：只处理 `tk2dCam` 下**除主相机以外**的子相机，并跳过
    嵌套的 `tk2dCamera`（它们自己会在 `UpdateCameraMatrix` 里翻转）；用
    `CameraProjectionMarker` 标记已翻转，避免和 `UpdateCameraMatrix` 双翻。
  - `StartScene` 还会清空 FSM 诊断缓存并调度交互修正。
- `BeginMirror` / `EndMirror` / `NegateVelocityY`：
  镜像上下文的统一入口，避免每个 wrapper 复制同样的逻辑。
- `Awake`：翻转 `localScale.y`；把负的 `SPEED_TO_ENTER_SCENE_DOWN` 改为正。
- `Update`：Reset 镜像上下文、确保 Superdash 补丁、顶部 kill plane、
  进入镜像、调用原版、finally 退出镜像并 tick 门/Cdash。
- `FixedUpdate`：Reset、进入镜像、原版、finally 退出并保证 gravityScale 为负。
- 简单 Hero wrapper：`Move`、`Jump`、`DoubleJump`、`Dash`、
  `CancelHeroJump`、`TakeDamage`、`BounceHigh`、`ShroomBounce`、
  `RecoilLeft/Right/RightLong/LeftLong/Down`、`FallCheck`、`JumpReleased`、
  `CanDreamNail`。全部使用 `try/finally`。
- `AffectedByGravity`：临时把 gravityScale 变正，让原版执行，
  再恢复负号；零保持为零；异常也会恢复。
- 重写的 Hero 碰撞/地面方法：
  - `CheckStillTouchingWall`：水平射线不变，`checkTop` 移到世界下方。
  - `CheckForBump`：探针移到顶部边缘，垂直射线从 hit 点下方 1 单位向**上**，
    高度比较采用屏幕空间语义。详见第 6 节。
  - `CheckNearRoof`：从 `bounds.min.y` 向下做对角/垂直探针。
  - `CheckTouchingGround`：从 three-point 位置向上。
  - `CheckTouching` / `CheckTouchingAdvanced`：down/up 对应的
    `top/bottom` 标签互换，左右不变。
  - `FindCollisionDirection`：调原版后交换 top/bottom。
- `GetGatePosition`：调原版后交换 top/bottom，并记录日志。
- `CameraProjectionMarker`：运行时组件，记录子相机是否已经一次性翻转。

### 3.2 `WrongGravity.Ground.cs`

- `FindGroundPoint` / `FindGroundPointY` 的 Hook。
- 逻辑：
  1. 用 `GroundProbe.TryFindUp` 在 Terrain 层向上找“天花板”；
  2. 找到则返回 Knight 的 transform 位置（collider 顶部贴住天花板）；
  3. 找不到则用 `GroundProbe.TryFindDown` 做修正后的向下搜索；
     上下都没有时才调用原版 `orig`（原版会记录自己的找不到地面错误）。
- 这个策略是刻意的：某些房间上方没有天花板，如果只做向上搜索会一直
  “找不到地面”；回退到向下搜索可以保持可用。
- 向下回退使用 `GroundProbe.TryFindDown`，它和 `TryFindUp` 使用同一套
  collider offset 修正；非场景入口时，只有上下都找不到才调用原版 `orig`。
- **场景入口例外**：`HeroController.EnterScene` 执行期间（`enteringScene`
  标志为 true）不做向上搜索，改用原版方向的向下搜索
  （`GroundProbe.TryFindDown`，保留 collider offset 修正）；找不到地面时才
  回退 `orig`。原版在门口会向下找地面；若改用镜像向上搜索，门上方刚好是
  低天花板时 Knight 会被放到天花板上，头部卡住，导致部分房间进门失败。
  该标志只在原版 EnterScene 协程的 `MoveNext` 同步执行期间为 true，不会
  影响长椅/危险重生后的镜像搜索。
- 交互修正器不使用 `TryFindDown`：交互点只向上找天花板，找不到就保留原位。
- 几何公式：
  - `scaleY = sign(transform.lossyScale.y)`
  - `worldOffsetY = collider.offset.y * scaleY`
  - 向上站立：`hit.y - 0.01 - extents.y - worldOffsetY`
  - 向下站立：`hit.y + 0.01 + extents.y - worldOffsetY`
  这些公式同时兼容 `localScale.y = -1` 和正常比例。

### 3.3 `WrongGravity.Interactions.cs`

- 在场景加载后延迟一帧扫描交互目标。
- **不再按 layer 19 过滤**。`INTERACTIVE_OBJECT = 19` 只是“交互对象”层，
  里面既有 NPC 本体、可调查物，也有非触发碰撞；上一版按层扫描会动错判定框。
- 目标识别改为：
  - `RestBench` 组件（长椅）；
  - `TransitionPoint` 且 `isADoor == true`（或名字含 `door`）的门型 transition；
  - `PlayMakerFSM` 的 `CallMethod` / `CallMethodProper` 调用
    `HeroController.CanTalk` / `CanInspect` / `CanInteract` 的对象
    （NPC 对话、可调查物、商店等）。
  - 跳过 Hero 自己的 FSM。
- 每个目标取自身及**直接子对象**上的 `isTrigger == true` 的 `BoxCollider2D`；
  跳过带 `DamageHero` 的危险触发器。
- 对每个触发器：
  0. 射线的起点不是框中心，而是 `box.bounds.max.y + 0.05`；
  1. 用 `Physics2D.OverlapBoxAll` 收集当前重叠的 Terrain collider；
  2. 如果框陷得比较深、上边缘仍在重叠地板内部，就把起点抬到该地板顶面
     之上；只对“中心在框中心下方”的 floor-like terrain 这样做，避免把
     起点抬到天花板上方；
  3. 从该起点只向上找天花板。Unity 的 Physics2D 起点在碰撞体内会在
     distance≈0 处命中，所以 `GroundProbe.FindNearest` 也会跳过
     distance≈0 的命中；
  4. 找不到天花板时保留原位。交互点只在向上方向有意义，不做向下回退，
     否则本来就在天花板上的框会被移到地板；
  2. 计算将触发器中心放到天花板点所需的 `offset.y`；
  3. 修改 `BoxCollider2D.offset`，而不是移动 NPC 本体；
  4. 如果 JSON profile 指定 `applySize`，修改 `size.y`。
- 使用 `HashSet<int>` 记录已处理实例，并给 collider GameObject 加
  `InteractionAdjustedMarker`；即使持久化对象跨场景保留，也不会重复叠加 offset。
- 场景名变化时清空缓存。
- JSON 的 `path` 支持精确匹配或后缀匹配。

### 3.4 `WrongGravity.Mirror.cs`

- `MirrorContext`：静态 depth；`Enter/Exit/Reset`。
- `MirrorEntity`：
  - `IsHero`：Knight 根对象或子对象；
  - `IsHeroFsm`：Hero 或其子对象上的 FSM；
  - `IsHeroTarget`：owner default target 是 Knight 根对象或带
    `HeroController` 的对象。

### 3.5 `WrongGravity.FsmMirror.cs`

- `CheckCollisionSide.CheckTouching` / `CheckCollisionSideEnter.CheckTouching`：
  - 只对 Hero FSM 生效；
  - 使用 Hero 本体第一个 `Collider2D`；
  - 不调用原版，手工做 3 条射线/边：
    - top：从 `bounds.min.y` 向 `Vector2.down`；
    - bottom：从 `bounds.max.y` 向 `Vector2.up`；
    - right/left：不变。
  - `ShouldCheck` 精确复刻原版 `OnEnter` 的 check 标志计算：
    `FsmBool 非 None 或 FsmEvent 非空`。
  - 射线长度 0.08；layer mask 使用 `1 << (layer.value & 31)`。
- `RayCast2d`：
  - 只对 Hero FSM、FSM 名含 `Superdash`、且 `cState.superDashing` 为 true；
  - 只镜像 local owner 的 `fromPosition.y` 和 `direction.y`；
  - 调用后在 `finally` 恢复；
  - 诊断日志用实例 hash 去重，场景切换时清空。

### 3.6 `WrongGravity.FsmVelocity.cs`

- `SetVelocity2d` / `SetVelocity2dIfFalse`：
  - 先调原版；
  - 满足“确实改变了 Y”条件时，翻转 Knight 的 `rb2d.velocity.y`。
- `GetVelocity2d`：
  - 只对 `Space.World`、Hero target、非镜像上下文生效；
  - 翻转输出 `vector.y` 和 `y`。
- `SetGravity2dScale`：
  - 对 Hero target 设置 `-Abs(value)`；
  - 0 保持 0。
- `DownSpellMode == 0` 时，FSM 名含 `Spell` 的不做速度/重力镜像。
- 所有 hook 都检查 `Config.Enabled` 和 `MirrorContext.IsActive`。

### 3.6.1 `WrongGravity.EnemyRecoil.cs`

- Hook `Recoil.RecoilByDirection(int direction, float magnitude)`。
- `DirectionUtils` 的方向编号：0=右，1=上，2=左，3=下。
- 对非 Hero 的 `Recoil` 组件，把 1 和 3 交换；0/2 不变。
- 覆盖路径：
  - `HealthManager.TakeDamage` 的击退；
  - `EnemyDreamnailReaction.RecieveDreamImpact`；
  - PlayMaker `BeginRecoil`；
  - `Recoil.RecoilByDamage`。
- 为什么只交换垂直：水平击退在镜像相机下左右不变；世界 +Y 在屏幕上是向下，
  所以世界向上的击退看起来是向下。
- 设置项：`Enemy Recoil`。
- 注意：这只修正**击退方向**；受击特效的旋转、`directionOfLastAttack`、
  部分敌人 FSM 的动画仍可能使用未镜像的世界方向。

### 3.7 `WrongGravity.Diagnostics.cs`

分成三块。

#### 门 / 场景切换

- `BeginSceneTransition`：记录 `[Door]`。源 gate 来自
  `TransitionPoint.lastEntered`，目标 gate/方向来自 `SceneLoadInfo`。
  注意：`lastEntered` 只在 `OnTriggerEnter2D` 的非 `isADoor` 路径赋值，
  所以门路径的源 gate 可能是旧值。
- `LeaveSceneHook`：把 GameManager 传来的 effective side 换回 world side，
  再调原版，保证 `transition_vel` 的 top/bottom 分支使用真实门方向。
- `EnterSceneHook`：
  - 保存 `gate.entryOffset`；
  - 调 `ApplyEntryOffset`；
  - 调原版拿到协程；
  - 用 `RestoreEntryOffset` 包装，协程结束后恢复；
  - 每次 `MoveNext` 前置 `enteringScene = true`，执行完立即清除，使新房间的
    `FindGroundPoint/Y` 跳过向上搜索、改走原版方向的向下搜索；
  - 如果 `ApplyEntryOffset` 或原版在返回协程前抛异常，也会恢复。
- `ApplyEntryOffset`：
  - DoorProfile 的 `applyEntryOffset` 优先；
  - 否则按 gate 名字的 world side：top -> -6，bottom -> -1.5。
  这是权宜之计：大多数门名符合 top/bot 约定，少部分用 JSON 覆盖。
- `TickTransition`：仅 `DROPPING_DOWN` 计时；超时通过反射调
  `FinishedEnteringScene(true, false)`。
- `GetTransitionTimeout`：DoorProfile 优先，否则全局 `TransitionTimeout`。
- `LogGateSide`：输出 world/effective side。

#### 顶部 kill plane

- `TickTopKillPlane` 每帧最多每秒扫描一次。
- `ApplyTopKillPlane`：
  - 找所有激活且名字含 `Bounds Cage` 的 `KillOnContact`；
  - 取每个笼子最宽的启用 `BoxCollider2D` 作为 bottom；
  - 找 `offset.y` 更大、size 相差 <=5 的 top；
  - 启用 top，缓存在列表中。
- 使用 local size/offset 而不是 `bounds` 匹配，因为 disabled collider 的
  bounds 在 Unity 中是零。
- 支持多个 Bounds Cage，而不是只取第一个。
- `DrawBlackBorders` 在场景过渡时也会刷新。
- 关闭设置时会把缓存的 top collider 全部 disable。

#### Crystal Dash / Superdash

- `EnsureSuperdashPatched`：
  - 找 Superdash FSM；
  - 检查 BUTTON UP / FINISHED 事件；
  - 检查 Ground Charged / Wall Charged 都有 `ListenForSuperdash`；
  - 检查四个 charge 状态存在；
  - 通过后才修改 FSM，避免半改。
- 对四个状态：
  - 清 `ListenForSuperdash.wasReleased`（仅 charged 状态）；
  - 禁用 `SendEventByName` 中发送 BUTTON UP 的 action；
  - 清除 public `FsmEvent` 字段中的 BUTTON UP；
  - 删除 FINISHED 转移；
  - 把 `FINISHED -> 目标状态` 插到索引 0。
- `TickSuperdash`：
  - 记录状态变化；
  - Charged 状态下，玩家真正松开 superDash 连续 2 帧后，
    由 mod 发送 FSM 自己的 BUTTON UP。
- 全局 FINISHED -> Dashing/Dash Start/Direction 若存在会被禁用。
- 依赖 PlayMaker 状态自转移语义；只在 1.5.78 的 FSM 结构上验证。

### 3.8 `WrongGravity.Settings.cs`

- `WrongGravityConfig`：所有持久化字段都有 XML 注释。
- `OnLoadGlobal`：`TransitionTimeout < 0.5` -> 3；DownSpellMode 限制 0/1。
- `GetMenuData`：10 个菜单项，包含互动修正、Debug Draw、Verbose Log。
- `Config.Enabled` 是启动开关；菜单文案明确要求重启。

### 3.9 `WrongGravity.Data.cs`

- `DoorProfile`：门覆盖。
- `InteractionProfile`：交互触发器覆盖。
- `MirrorData`：
  - 从嵌入资源加载 JSON；
  - 建大小写不敏感字典；
  - `GetDoorTimeout`；
  - `FindInteraction` 支持精确/后缀 path 匹配。
- `MiniJson`：手写递归下降解析器，支持对象、数组、字符串、数字、
  布尔、null；不依赖 Newtonsoft。
- 解析失败不会崩溃：`Load` 捕获异常、记录日志、返回空数据。

### 3.10 `Data/WrongGravityData.json`

当前内容：

```json
{
  "doors": [
    { "scene": "Tutorial_01", "gate": "top1", "forceControlAfter": 1.0 },
    { "scene": "Cliffs_02", "gate": "bot2", "forceControlAfter": 1.0 }
  ],
  "interactions": []
}
```

- 只使用 forceControlAfter。
- 没有 applyEntryOffset，所以走 fallback 的 -6/-1.5。
- interaction 为空，交互修正器按组件/FSM 识别目标后自动地面放置。
- entryVelocity / applyEntryVelocity 目前只是解析，未应用。

### 3.11 `WrongGravity.csproj`

- `net472`、`Version 1.0.0.8`。
- 引用游戏 DLL、MMHOOK、UnityEngine、Vasi、PlayMaker。
- 把 JSON 嵌入为 `WrongGravity.Data.WrongGravityData.json`。
- Release 目标：
  - 复制 DLL/PDB 到 `Output/`；
  - 复制 `$(ProjectDir)README.md`（不是 SolutionDir；直接 build csproj 时
    SolutionDir 是 `*Undefined*`）；
  - 打 zip；
  - 删除 `Output/`；
  - 打印 SHA256。

### 3.12 `.github/workflows/dotnet.yml`

- tag push 触发。
- `windows-latest` + .NET 8 SDK（net472 在 Linux 上需要额外 reference
  assemblies，Windows 更稳）。
- `dotnet restore`、`dotnet build -c Release`。
- 用 `gh release create` 上传 `WrongGravity.zip`（不再使用已废弃的
  create-release/upload-release-asset v1）。

---

## 4. 镜像数学与示例

### 4.1 速度

```text
真实世界：gravity = (0, -9.81), gravityScale = -1
          => 加速度 = (0, +9.81)  // 向上

原版 HeroController 认为 +Y = 上。
进入 mirror：velocity.y = -velocity.y
离开 mirror：velocity.y = -velocity.y
```

### 4.2 地面点

设：

- `e = collider.bounds.extents.y`
- `o = collider.offset.y`
- `s = sign(transform.lossyScale.y)`

世界中心偏移为 `o*s`。

向上站立（collider 顶部贴天花板）：

```text
top = transform.y + o*s + e = hit.y - 0.01
transform.y = hit.y - 0.01 - e - o*s
```

向下站立（collider 底部贴地面）：

```text
bottom = transform.y + o*s - e = hit.y + 0.01
transform.y = hit.y + 0.01 + e - o*s
```

这也是 `GroundProbe` 两个公式的来源。

### 4.3 相机

```csharp
camera.projectionMatrix *= Matrix4x4.Scale(new Vector3(1f, -1f, 1f));
```

tk2d 使用正交投影，右乘 Y 缩放会翻转屏幕 Y 轴。主相机只由
`UpdateCameraMatrix` 翻转；`StartScene` 只翻转其他子相机，避免双重翻转。

---

## 5. 门的方向语义

### 5.1 为什么 GetGatePosition 要交换

- `TransitionPoint.OnTriggerEnter2D` 用 `GetGatePosition` 设置
  `SceneLoadInfo.HeroLeaveDirection`。
- `HeroController.EnterScene` 用 `GetGatePosition` 设置
  `HeroController.gatePosition`，决定从上方/下方/左侧/右侧进入。
- `TransitionScene` 也可能读取它。

因此 hook 全局交换 top/bottom，让游戏按“屏幕上的方向”处理进入。

### 5.2 为什么 LeaveScene 要换回来

`GameManager` 把 effective side 传给 `HeroController.LeaveScene`。
原版 `LeaveScene` 用这个值设置 `transition_vel`：

- top：`(0, +MIN_JUMP_SPEED)`
- bottom：`(0, 0)`
- left/right：`(∓RUN_SPEED, 0)`

如果直接用 effective side，真实门方向会错。所以 `LeaveSceneHook` 先换回
world side，再调原版。

### 5.3 entryOffset 的权宜规则

`ApplyEntryOffset` 用 gate 名字的 world side 做默认：

- top -> -6
- bottom -> -1.5

这不是完美几何，但大多数门名符合 `top1`/`bot2` 约定；异常门用
DoorProfile 覆盖。代码注释里也明确写了这是权宜之计。

---

## 6. CheckForBump 的镜像细节

原版（简化）：

```text
探针在 collider 底部附近（min.y）
向外水平射线
如果外侧命中、内侧不命中：
    从 hit.point + (x, +1) 向 下 打 1.5
    比较两个 hit 的高度
```

镜像后：

```text
探针在 collider 顶部附近（max.y，脚的位置）
向外水平射线
如果外侧命中、内侧不命中：
    从 hit.point + (x, -1) 向 上 打 1.5
    比较两个 hit 的屏幕高度（世界 Y 取反）
```

旧代码只把方向改成 up，仍然用 +1，导致射线从台阶上方开始，
几何上会错过台阶。当前实现修成 -1，并反转高度比较符号。
这个改动没有在真实游戏中逐门验证过，属于“按几何推导的修复”，
建议优先测试台阶、平台边缘和墙体 bump。

---

## 7. 交互修正

### 7.1 自动模式

目标不是按 layer 找的，而是按行为/组件找：

- `RestBench`：长椅；
- `TransitionPoint.isADoor` 或名字含 `door`：门型 transition；
- 调用 `HeroController.CanTalk` / `CanInspect` / `CanInteract` 的
  PlayMaker FSM：NPC 对话、调查物、商店等。

对每个目标：

- 取自身和**直接子对象**上的 `BoxCollider2D`；
- 只处理 `isTrigger == true`，且没有 `DamageHero` 的；
- 射线起点抬到框顶上方；如果框深陷在地板里，再抬到重叠地板顶面之上；
- 只向上搜索天花板，跳过 distance≈0 的起点命中；
- 找不到天花板时保留原位；不做向下回退，避免把天花板上的框移到地板；
- 把 collider 的 `offset.y` 调到天花板点，保持 X 不变；
- 不移动 NPC 本体，只移动触发器；
- 每个实例只处理一次；持久化对象用 `InteractionAdjustedMarker` 标记，跨场景不会重复叠加。

### 7.2 JSON 覆盖

```json
{
  "scene": "Town",
  "path": "Sly/Interaction",
  "offsetY": 5.0,
  "sizeY": 2.0,
  "applyOffset": true,
  "applySize": true
}
```

- `path` 可以是完整路径，也可以是后缀。
- profile 不只用于覆盖位置，也会作为**发现目标**：即使组件/FSM 启发式识别
  不到，也可以按 path 直接找到对象。
- `offsetY` 是相对天花板点的偏移。
- `sizeY` 会替换 BoxCollider2D 的高度。
- 如果自动放置不对，优先加 profile，而不是改代码。

### 7.2.1 半个卡在地里的触发器

- 某些 prefab 的交互框故意有一半嵌在地形里。Unity 的
  `Physics2D.queriesStartInColliders` 默认是 true，所以从框中心向上的射线会
  在 distance=0 处命中它嵌入的那块地板；如果当成“天花板”，框就会被移到
  地板下面。
- 修正器不把射线起点放在框中心，而是放在框顶上方；如果框仍陷在地板内部，
  再把起点抬到重叠地板顶面之上。
- 这样射线不会在 distance=0 处命中嵌入地板，而是继续向上找真正的天花板。
- 如果框已经和“中心在框中心上方”的 Terrain 重叠，说明它已经在天花板一侧，
  直接保留原位，不再寻找更高的表面。
- 找不到天花板时保留原位置；不做向下回退，避免把原本在天花板上的框移到地板。
- 这个处理只改 `BoxCollider2D.offset`，不改 NPC/门本体的位置。

### 7.3 已知风险

- FSM 识别依赖 `CallMethod` / `CallMethodProper` 的方法名；如果某个 NPC 用
  自定义 action 或把方法名放在变量里，可能识别不到，需要在 JSON 里补 profile
  或后续扩展识别规则。
- 门型 transition 的触发器移动后，`TransitionPoint.OnTriggerEnter2D` 的触发位置
  会改变；需要实测门型 transition 是否仍能正常进入。
- 如果某个触发器没有 ceiling/floor，修正器会跳过并记录
  `[Interaction]` 警告。
- 对非 BoxCollider2D 暂不处理。

---

## 8. 顶部 kill plane

### 8.1 为什么不能按 bounds 选

Unity 中 disabled Collider2D 的 `bounds` 是零。原版 `Bounds Cage` 的
顶部 collider 默认是 disabled，所以不能通过 `bounds` 或高度选。

### 8.2 选择算法

1. `FindObjectsOfType<KillOnContact>()`。
2. 过滤 `isActiveAndEnabled` 且名字含 `Bounds Cage`。
3. 取每个笼子最宽的启用 BoxCollider2D 作为 bottom。
4. 在同一个 GameObject 的 BoxCollider2D 中找：
   - `offset.y > bottom.offset.y`
   - `|size.x - bottom.size.x| <= 5`
   - `|size.y - bottom.size.y| <= 5`
5. 启用 top，并缓存。
6. 多个 Bounds Cage 会全部处理。

### 8.3 限制

- top collider 必须在同一个 GameObject 上；如果在子对象上，找不到。
- 如果场景中有多个 Bounds Cage 且它们不属于当前区域，会全部启用顶部
  collider；目前认为每个笼子都是场景级 kill plane，影响可接受。
- 找不到时只记录 warning，不创建新 collider。

---

## 9. Crystal Dash 补丁

### 9.1 问题

原版 `Ground Charged` / `Wall Charged` 状态里存在 FINISHED 转移，
以及某些 action 会自动发送 BUTTON UP。在反转重力/大空间下，Knight 可能
在仍按住按钮时被自动释放。

### 9.2 补丁步骤

1. 找到 Superdash FSM 和 BUTTON UP / FINISHED 事件。
2. 验证 Ground Charged / Wall Charged 都有 ListenForSuperdash。
3. 对四个状态：
   - 清 charged 状态的 `wasReleased`；
   - 禁用发送 BUTTON UP 的 SendEventByName；
   - 清 public FsmEvent 字段中的 BUTTON UP；
   - 删除 FINISHED 转移；
   - 插入 FINISHED -> 目标状态（charged -> 自身，charge -> charged）。
4. 关闭全局 FINISHED -> Dashing/Dash Start/Direction。
5. 订阅 StateChanged 做日志。
6. 每帧检查按钮：连续 2 帧未按下才发送 mod 自己的 BUTTON UP。

### 9.3 限制

- 状态名和事件名是硬编码的英文名，只适用于 1.5.78。
- 依赖 PlayMaker 自转移行为；如果 FSM 结构变化，补丁会跳过并记录
  warning，而不是继续半改。
- `Config.Enabled` 关闭后补丁不会自动还原；重启游戏即可。

---

## 10. 配置与数据

### 10.1 设置

见 README 的设置表。所有字段都在 `WrongGravity.Settings.cs` 的
`WrongGravityConfig` 中有 XML 注释。

### 10.2 门 profile

- `scene` + `gate` 是 key。
- `forceControlAfter` 只影响 DROPPING_DOWN watchdog。
- `entryOffsetX/Y` + `applyEntryOffset` 已实现。
- `entryVelocityX/Y` + `applyEntryVelocity` 仍保留给下一版。

### 10.3 交互 profile

- `scene` + `path` 是 key。
- `offsetY` 和 `sizeY` 分别受 `applyOffset` / `applySize` 控制。
- path 支持后缀匹配。

---

## 11. 日志

统一使用 Modding API 的 `Log` / `LogWarn` / `LogError`：

- `[Door]`：门、LeaveScene、entry offset、watchdog。
- `[KillPlane]`：Bounds Cage 选择。
- `[FSM]`：CheckCollisionSide、RayCast2d、velocity。
- `[Cdash]`：Superdash 状态和 hold/release。
- `[Ground]`：ground search（Verbose Log）。
- `[Bump]`：bump 高度（Verbose Log）。
- `[Interaction]`：交互触发器调整。

旧的 `Debug.Log` / `Debug.LogError` 已移除；只有 `Debug.DrawLine` /
`Debug.DrawRay` 在 `Debug Draw` 开启时使用。

---

## 12. 构建与发布

```powershell
cd D:\HKProjects\WrongGravity\HollowKnight.WrongGravity
dotnet build WrongGravity.csproj -c Release --nologo
```

Release 目标会：

1. 复制 DLL/PDB 到 `Output/`；
2. 复制 README；
3. 打 `WrongGravity.zip`；
4. 删除 `Output/`；
5. 打印 SHA256。

当前版本：`1.0.0.8`。

Release 构建结果：

```text
0 warnings
0 errors
WrongGravity.zip
SHA256 615B88E6F0902018E1A06C741397142940B607CAC3C3593041CE04D0892CC357
```

当前 zip 内容：

```text
WrongGravity.dll
WrongGravity.pdb
README.md
```

JSON 不单独出现在 zip 中，它已作为 EmbeddedResource 嵌入 DLL。

---

## 13. 测试清单

### 13.1 基础

- [ ] 普通跳跃 / 二段跳 / 冲刺 / 超级冲刺
- [ ] 受击 Recoil / 下砸 Recoil
- [ ] 骨钉向上/向下击中敌人：敌人应向屏幕外侧击退
- [ ] 敌人受击特效方向（可能仍不镜像）
- [ ] 梦钉 / 梦门
- [ ] 法术（尤其下砸）
- [ ] 长椅 / NPC / 石碑 / 商店交互
- [ ] 水面 / 酸液
- [ ] 各种上/下/左/右门
- [ ] 场景边缘向上掉出（顶部 kill plane）
- [ ] 左右门/门型门进入后的落地点（EnterScene 内按原版方向向下搜索）

### 13.2 Cdash

- [ ] 蓄满后按住不自动 dash
- [ ] 松开后正常 dash
- [ ] 墙面蓄力 / 空中蓄力
- [ ] 狭小空间和开放空间

### 13.3 交互

- [ ] 自动放置的 bench / NPC
- [ ] JSON profile 覆盖的异常交互点
- [ ] 长椅、NPC 对话、调查物、商店的触发器被移动到正确一侧
- [ ] 门型 transition（isADoor）仍能正常触发
- [ ] 一半嵌在地形里的交互框不会被移动到地板下面
- [ ] 非交互的 layer 19 对象不再被错误移动

### 13.4 门

- [ ] Tutorial_01 top1
- [ ] Cliffs_02 bot2
- [ ] 左右门
- [ ] 低天花板房间从门口进入（不再卡头）
- [ ]  Dream gate / bench transition
- [ ] watchdog 不误触发

---

## 14. 已知限制

- 只支持 HK 1.5.78.11833。
- 水面、酸液、粒子、上吼/下砸对象没有完整镜像。
- 敌人垂直击退已交换；但受击特效旋转、部分 Quake/boss FSM 动画仍可能使用原版方向。
- RayCast2d 只覆盖 Superdash + active superDashing。
- GetVelocity2d 只覆盖 Space.World。
- FindGroundPoint 在场景入口期间（EnterScene）按原版方向向下搜索
  （TryFindDown，含 offset 修正）；其余情况在上下都找不到时才回退到原版。
  没有天花板/地板的房间仍可能报原版错误。
- 交互修正基于组件/FSM 行为识别；无法识别的自定义交互对象需要 JSON 覆盖或扩展规则。
- 门 entryOffset 默认规则基于 gate 名字，异常门需要 DoorProfile。
- Bounds Cage 的 top collider 必须在同一个 GameObject 上。
- 不是所有可能的 HeroController 入口都经过 mirror wrapper；新增入口必须
  遵守 `BeginMirror/EndMirror` 模式。

---

## 15. 从 1.0.0.2 到 1.0.0.8 的改动

- 重构所有源文件，补充 XML 文档和行内注释。
- `FindGroundPoint` / `FindGroundPointY` 改为“向上优先，修正后的向下回退，最后才用原版”。
- 新增场景入口地面搜索例外：`EnterScene` 执行期间 `FindGroundPoint` /
  `FindGroundPointY` 改为按原版方向向下搜索（`TryFindDown`，含 offset 修正），
  修复部分低天花板房间进门卡头。
- `CheckForBump` 修正垂直探针起点（+1 -> -1）和高度比较方向。
- 相机改为“主相机只由 UpdateCameraMatrix 翻转；StartScene 只翻其他子相机”，
  并移除双重翻转。
- 所有 Hero wrapper 使用 `try/finally`。
- `MirrorContext.Reset()` 保留，但文档明确它不恢复 velocity。
- 顶部 kill plane 支持多个 Bounds Cage，并缓存/禁用。
- Cdash 补丁增加前置校验，避免半改 FSM。
- 新增交互/门型 transition 触发器自动修正 + JSON profile；不再使用 layer 19 过滤。
- 新增交互框 Terrain 重叠检测：射线起点抬到框顶/地板顶之上，只向上找
  天花板；找不到则保留原位，不做向下回退。
- 新增敌人垂直击退镜像（RecoilByDirection 交换 up/down）。
- 新增 `FixInteractions`、`LogInteractions`、`Debug Draw`、`Verbose Log`。
- 移除 Unity `Debug.Log` / `Debug.LogError`，保留可选的 DrawLine/DrawRay。
- csproj 修正 README 复制路径（ProjectDir），版本号 1.0.0.8。
- CI 改为 Windows + `gh release create`，避免已废弃的 release action。
- README/DEVELOPMENT 重写，修正旧文档中与代码不一致的描述。

---

## 16. 尚未完成的目标

本节明确列出还没有完成或还没有经过充分实测的内容。它不是承诺，只是当前
代码和测试状态的真实清单。

### 16.1 交互与门（优先级最高）

- **更多交互对象识别**：当前识别 `RestBench`、door-type `TransitionPoint`、
  以及 `CallMethod` / `CallMethodProper` 调用 `CanTalk` / `CanInspect` /
  `CanInteract` 的 FSM。自定义 NPC、商店、石碑如果使用别的调用方式，仍可能漏识别。
- **非 BoxCollider2D 触发器**：交互修正只处理 BoxCollider2D；Circle/Polygon/
  Capsule 等还没有实现。
- **更深的触发器层级**：当前只扫描目标自身和直接子对象；触发器在更深层时
  需要 JSON profile 指到那个子对象，或扩展扫描规则。
- **普通 transition gate**：目前只移动 door-type（`isADoor` 或名字含 door）
  的判定框；普通上下左右门的判定框是否也需要移动，还没有系统测试。
- **door-type transition 的完整回归**：门型触发框移动后，门动画、进入方向、
  `entryOffset`、watchdog 都需要逐门验证。
- **半嵌交互框**：现在会抬高射线起点到框顶/地板顶之上，只向上找天花板；
  还需要在更多场景验证不会把已经位于天花板的框误判。
- **交互 profile 的自动发现**：JSON path 可以作为目标发现手段，但 path 匹配
  目前是后缀匹配；复杂层级/同名对象可能需要更精确的匹配。
- **`CanTalk/CanInspect/CanInteract` 的其它调用来源**：目前只扫描 PlayMaker
  `CallMethod`；如果游戏用 UnityEvent、SendMessage 或反射调用，不会被识别。

### 16.2 视觉 / 法术 / 水

- 水面、酸液、熔岩的 surface / 溅射 / 死亡位置还没有镜像。
- 上吼、下砸、Quake 的对象朝向和冲击方向还没有完整镜像。
- 粒子、trail、命中特效、部分法术对象仍是原版方向。
- `DownSpellMode == 1`（Screen Down）仍只是实验开关，没有完整实现。
- 敌人受击特效的旋转、`HealthManager.directionOfLastAttack` /
  `GetAttackDirection()`、部分 Boss 的自定义受击动画还没有镜像。

### 16.3 稳定性 / 兼容性

- `CheckForBump` 的几何修复是按原版 IL 推导的，还没有在真实游戏里逐种台阶/
  平台/墙体验证。
- `FindGroundPoint` / `FindGroundPointY` 采用“向上 → 修正向下 → 原版”的
  回退；没有天花板/地板的房间仍可能产生原版错误。场景入口期间按原版方向
  向下搜索，但仍需逐房间验证落地点。
- 场景入口改为向下搜索后，Knight 可能先落在原版地板上，再由反转重力拉起；
  这是为避开门口低天花板卡头而接受的过渡状态。
- 相机目前只处理主 tk2d 相机和 StartScene 的其它子相机；额外相机、HUD、
  菜单相机、嵌套 tk2dCamera 还需要回归测试。
- `Invert Gravity` 是启动开关；运行时关闭不会完整恢复 Hero 的 localScale/
  `SPEED_TO_ENTER_SCENE_DOWN`，必须重启。
- 只支持 Hollow Knight 1.5.78.11833；大量 FSM 状态名、事件名、私有字段名
  是硬编码的。
- `entryVelocityX/Y` / `applyEntryVelocity` 仍只解析，没有应用。

### 16.4 工程 / 测试

- 没有自动化测试；`dotnet build` 只能验证编译，不能验证游戏内行为。
- CI 的 Windows Release 构建只做了本地编译验证，GitHub Actions 实际运行
  还需要在 tag 上确认。
- 测试清单中的交互、门、Cdash、法术、水面项目仍需逐项勾选。
- 文档和 release hash 需要随每次代码改动更新。
- `WrongGravity.zip` 不进入 git；发布包由 Release target/CI 生成。

## 17. 许可

GPLv3。见 `LICENSE`。
