# 公共联机层接入说明

适用于当前开发分支的源码。仓库既有 `Release` 包不代表已包含这些改动；使用前须确认所有参与端实际安装的 RocketLib、BroMaker 和角色版本。

2026-11-07 主线范围收口为 RocketLib 公共能力与 BroMaker 框架接入。已有消费者用于提供有限复用证据；其他角色的迁移、技能和伤害适配由其作者负责，不作为公共库阶段交付的前置。

## 能力与责任

| 公共层提供 | 使用方负责 |
| --- | --- |
| 模组版本与扩展能力清单 | 定义自己的能力版本、支持条件和不支持时的行为 |
| 稳定 prefab ID、工厂与前缀 resolver | 构建可用 prefab、保持 ID 与组件结构一致 |
| 接入游戏网络生成、完整 NID/owner 注册及生成缓冲清理 | 在正确拥有端发起一次生成，按对象规则销毁 |
| 普通 fire 的累计边沿记录与有条件的带时间镜像执行 | 保留原生输入路径，适配角色专属选择、状态、技能及结算 |

网络对象在两端各有一个副本，不代表同一次命中已保证只结算一次伤害。使用 `UnityEngine.Object.Instantiate` 或本地生成接口产生的对象，也不会自动改成网络对象。

## BroMaker 角色

使用配套 BroMaker 开发分支时，从该仓库的 `docs/networking.md` 开始。它已接入角色生成、名称/槽位/variant 和自定义 projectile/grenade/sachel 的公共注册路径。角色作者通常沿框架创建 prefab 和游戏网络生成入口使用，无需另写一套注册、生成 RPC 或伪造 NID。

## 直接使用 RocketLib

接口位于 `RocketLib.Network`：

| 接口 | 用途 |
| --- | --- |
| `ModNegotiation.RegisterMod(modId, version)` | 注册自己的模组与版本 |
| `SetExtensionData(modId, key, byte[])` / `GetExtensionData(peer, modId, key)` | 发布和读取自定义能力或状态协议的说明 |
| `GetModVersion(peer, modId)` / `GetAllPlayers()` | 检查当前参与端版本与清单 |
| `HasReceivedManifest(peer)` | 区分清单尚未收到和清单已收到但未安装某模组；本端清单视为已知 |
| `NetworkPrefabs.RegisterFactory(id, factory, onInstantiated)` | 为一个稳定 ID 提供可缓存的 prefab 工厂 |
| `RegisterInstance(id, prefab, onInstantiated)` | 将本地发起生成使用的 prefab 对象映射到 ID |
| `RegisterResolver(prefix, resolver, onInstantiated)` | 按 ID 前缀及 publisher 查找或构建接收端 prefab |
| `SerializeIdList(entries)` / `GetPrefabEntry(id, publisher)` | 发布或查询 prefab 与组件清单 |
| `AllPlayersHavePrefab(id)` | 查询当前远端清单是否包含该 ID |
| `OnResolveFailed` | 观察 ID、publisher 与解析失败原因 |

`RegisterFactory` 或 `RegisterResolver` 负责接收端解析；`RegisterInstance` 负责发起端 prefab 对象到 ID 的映射。只做其中一项不等于完整接入。ID 使用自己的稳定命名空间；不要使用本次运行的实例号或 NID 作为 prefab ID。

接入顺序：

1. 各端正常加载模组后注册 modId/version，构建 prefab，登记实例映射及工厂或 resolver。
2. 将所有对外提供的 prefab 汇总为 `PrefabIdEntry`，通过 `ModNegotiation.SetExtensionData(modId, "prefabs", NetworkPrefabs.SerializeIdList(entries))` 发布。该调用替换此模组的整份 `prefabs` 扩展，不逐条追加。
3. 组件信息用实际兼容的组件完整类型名及顺序表达；`componentNames=null` 表示尚未知，接收路径会拒绝解析，不能当成已完成注册。
4. 确认需要的端具备相应版本、能力与 prefab 后，由约定拥有端沿游戏的 `InstantiationController.NetworkedInstantiate` 或相应网络生成入口发起一次。已登记实例会被公共路径识别；无需直接调用内部 `CustomInstantiation`。
5. 接收端通过 publisher 清单和本地工厂/resolver还原 prefab，再走注册与 `NetworkSetup`。`onInstantiated` 用于实例激活等必要初始化，不再生成第二份对象。

`AllPlayersHavePrefab` 是清单查询，不核对完整玩法协议、最终组件登记或伤害；没有远端时也不能将其返回值作为双端成功证据。解析缺失不会自动向对方下载模组。动态组件或自定义生成参数必须由使用方提供双方一致的构建与解释规则。

## 公共输入的范围

RocketLib 在 UMM 正常初始化时安装公共输入接入，角色作者不应重复安装同一套 Hook，也不应手动补发同一次 fire。当前只覆盖经过 `TestVanDammeAnim.Update` 公共虚 `CheckInput` 调用点的同步英雄普通 fire；完全绕过该路径的角色需要作者另行接入。

| 模式 | 条件与保证 |
| --- | --- |
| 原 State/InputBits | 未具备相应协商能力时保留原路径 |
| RSI v1 | 所有实际远端支持 `RocketLib/short-input-records=[1]`；记录累计边沿，不新增镜像执行保证 |
| RSI v2 | 还须全部支持 `RocketLib/short-input-timed-execution=[1]`；携带源边沿时间，在原生过滤路径中按序消费 |

v2 的历史与待消费队列各限 64 沿；短 hold/gap 受接收帧量化，迟到 release 可能拉长持键。窗口缺口、溢出、失效时钟或生命周期中断会取消对应执行，不无限重试。不承诺严格时长、每 press 一发或逐发一致。

`ShortInputSync.CanTransmitRecords` / `CanExecuteTimed` 查询能力；`TryGetSnapshot` 和 `TryGetExecutionSnapshot` 提供记录、选择、实际消费水位与取消原因。它们是输入观测接口，不是子弹、命中或伤害计数。完整 NID、owner、实例与连接生命周期必须连续；当前不提供同 NID 恢复或 owner 迁移的 epoch 协议。

## 作者自己的事件与结算

随机选出的物品、技能种类、弹药和成功/取消结果，应先定义拥有端与具体事件，再利用既有能力协商及游戏 RPC 传递必要参数。不要让两端各自随机后只同步按键，也不要把取物提交直接套作公共 fire 或投掷协议。

至少明确：事件绑定的完整 NID/owner、动作身份、真实发送者、重复与过期处理、成功/取消条件，以及退场、死亡和换角后旧事件如何拒绝。伤害按该机制与原生网络路径确定唯一最终结算责任；生成副本一致只能证明对象链。

## 已有验证与交付边界

公共层已有实际游戏程序集编译、离线协议/逻辑验证，以及两端角色、投射物、fire 和兼容路径的有限实测。Aquabro 提供首个消费者证据；Drunken 的 Crate 生成、取物提交、有限生命周期与正常投掷清空提供另一类复用证据。历史失败按当时版本保留，当前成功不追认旧版本。

这不覆盖所有角色、所有技能、共享目标伤害、完整战斗、Boss、异图或三端房主迁移。作者应针对自己的机制验证双向拥有端、同对象身份、实际状态/结算与退出清理；无需复制全部历史测试。公共层出现可复现缺陷时，可提供使用的版本、完整对象身份和有限重现步骤。
