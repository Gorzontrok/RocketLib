# RocketLib

[Thunderstore Page](https://thunderstore.io/c/broforce/p/RocketLib/RocketLib/)  
This is a collection of tools I use in my mods.

**What's Inside :**  

* A screen logger with color and times (F7 to clear).
* Fix for avoiding the mod manager for being destroy
* Fix the bug where the cursor in LevelEditor was stuck.

## Installation

* The all and [latest](./Release/RocketLib.zip) release are in the [Release](./Release/) folder.

## 联机开发

当前开发分支包含模组协商、稳定 prefab ID 与公共网络生成、普通 fire 输入记录及有条件的带时间镜像执行。作者入口见[公共联机层接入说明](docs/networking.md)。配套 BroMaker 负责框架接入，角色专属状态、技能和伤害由角色作者适配。

已有双端有限验证；开发分支源码与既有 `Release` 包应分别看待。2026-11-07 起主线按公共库与框架收口，不再追加消费者改造。

## Know Issue

* 共享目标伤害、完整角色/技能、Boss、异图和三端迁移未获全面验收，具体输入和生命周期限制见接入说明。

## Incompatible

* 需要按模组版本、prefab 清单与具体能力判断；仅安装 RocketLib 不代表所有第三方角色已支持联机。
