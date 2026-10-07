# 清单格式（MANIFEST-SCHEMA）

`manifest.json` 是 LanPE 的发布真源，随每个 Release 一起上传。打包器默认拉取
**latest release** 的该资产；也支持自定义 URL 或本地文件。

## 顶层字段

| 字段 | 类型 | 说明 |
|---|---|---|
| `schemaVersion` | int | 清单格式版本，当前为 `1` |
| `name` | string | 套件名（`LanPE`） |
| `version` | string | 套件版本（semver） |
| `releaseTag` | string | 对应 Release 标签（`v1.0.0`） |
| `generatedAt` | string(ISO8601) | 生成时间（UTC） |
| `toolchain` | object | 工具链资产（见下） |
| `components` | array | 组件列表 |

### toolchain

| 字段 | 说明 |
|---|---|
| `file` | 资产文件名（`lanpe-tools.zip`） |
| `url` | 下载地址 |
| `sha256` | 校验值 |
| `sizeBytes` | 字节数 |

## 组件（components[]）

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | string | 唯一标识（`win11pe` / `win10pe` / `linux-rescue`） |
| `name` | string | 显示名 |
| `kind` | `winpe` \| `linux` | 组件类型 |
| `version` | string | 版本 |
| `arch` | string | 架构（`x64`） |
| `description` | string | 说明 |
| `defaultSelected` | bool | 默认是否勾选 |
| `payload` | object | 所需资产，键名自定（`media` / `iso`） |
| `boot` | object | 引导参数 |
| `options` | array | 可配置启动选项 |
| `software` | array | 内置软件包（可独立勾选） |

### payload（资产引用）

每个值是一个资产引用对象：

| 字段 | 说明 |
|---|---|
| `file` | 文件名（可为相对路径，如 `apps/diskgenius.zip`） |
| `url` | 下载地址 |
| `sha256` | 校验值 |
| `sizeBytes` | 字节数 |

### boot

| 字段 | 说明 |
|---|---|
| `mode` | `bcd-chainload` \| `loopback` \| `wimboot` |
| `dir` | （winpe）介质内目录，如 `pe11` |
| `wimSubPath` | （winpe）wim 相对路径，如 `sources/boot.wim` |
| `isoPath` | （linux）ISO 在介质内的路径 |

### options[]（启动选项）

| 字段 | 说明 |
|---|---|
| `id` | 选项标识 |
| `label` | 显示名 |
| `type` | `bool` \| `choice` \| `string` |
| `default` | 默认值 |
| `choices` | （choice）候选值数组 |

### software[]（内置软件）

| 字段 | 说明 |
|---|---|
| `id` | 唯一标识 |
| `name` | 显示名 |
| `defaultSelected` | 默认勾选 |
| `file` | 资产文件名（`apps/xxx.zip`） |
| `url` | 下载地址 |
| `sha256` | 校验值 |
| `sizeBytes` | 字节数 |
| `archive` | `zip` \| `7z` \| `none`（直拷） |
| `extractTo` | 介质内解压目标，如 `Apps/DiskGenius` |

## 生成方式

`scripts/gen-manifest.ps1` 扫描 `release/assets/` 下所有资产，计算 `sha256`/`sizeBytes`，
结合 `release/manifest.template.json` 的元数据输出最终 `manifest.json`。

## 示例

见仓库 `release/manifest.json`。
