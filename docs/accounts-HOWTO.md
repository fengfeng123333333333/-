# 账号清单填写说明

## 三条前提（先看，能省掉返工）

**1. 不要用日常 Chrome 配置跑自动化。**
本机目前只有一个 Chrome `Default` 配置，那是日常号。拿它跑批量操作，等于拿主账号冒封号风险，而且自动化启动的浏览器会往同一个配置里写 Cookie，登录态会被搅乱。**每个自动化账号一个独立 user-data-dir**，互不相干。

**2. `bit` 通道不填路径，填窗口 ID。**
比特浏览器的配置由它自己管理，我们不碰它的文件。我们只需要"窗口 ID"（一串 32 位十六进制），通过本地接口 `http://127.0.0.1:54345` 的 `browser.open` 拿到 ws 端点。所以 `google` 行填 `user-data-dir`、`bit` 行填 `比特窗口ID`，另一列留空。

**3. 密码不写进 CSV。**
原版把主页密码明文存在 `settings.json` 里，这是它一个明显的问题。我们这版只存**引用名**（`cred://acc01`），真实密码用 Windows DPAPI 加密存在仓库外的 `secrets/` 目录，由程序的"设置密码"入口写入。

## 逐列说明

| 列 | 填什么 | 怎么拿到 | 可否留空 |
| :-- | :-- | :-- | :-- |
| `别名` | 英文小写短名，如 `acc01` | 自定，**全表唯一** | 否 |
| `通道` | `google` 或 `bit` | 看你用哪种浏览器环境 | 否 |
| `user-data-dir` | Chrome 独立配置目录，如 `C:\FbProfiles\acc01` | 见下方"新建独立配置" | `bit` 行留空 |
| `比特窗口ID` | 32 位十六进制串 | 比特浏览器→窗口列表→对应窗口的 ID | `google` 行留空 |
| `代理` | `http://127.0.0.1:7890` / `socks5://127.0.0.1:1080` | 你的代理软件设置里看 | 可空＝直连 |
| `主页ID` | Facebook 主页的数字 ID | 主页后台网址里的那串数字 | 可空（运行时读取） |
| `主页名称` | 主页显示名 | 自己看得懂就行 | 可空 |
| `凭证引用` | `cred://acc01` | 约定写法，与别名一致即可 | 可空 |
| `状态` | `待接入` / `正常` / `停用` | 手动维护 | 否 |
| `备注` | 任意说明 | — | 可空 |

## 新建一个独立的 Chrome 配置（google 通道）

不用装任何东西，一条命令就能开一个完全隔离的浏览器（首次需要手动登录一次 Facebook）：

**云主机上直接执行（cmd）**：

```bat
mkdir C:\FbProfiles\acc01
"C:\Program Files\Google\Chrome\Application\chrome.exe" --user-data-dir="C:\FbProfiles\acc01" --no-first-run --no-default-browser-check https://www.facebook.com/
```

> 后两个参数不能省：少了 `--no-first-run` 每次都会弹"设为默认浏览器"，自动化接管时会被这个弹窗挡住。

打开后登录对应的 Facebook 账号，**然后正常关闭窗口**。目录 `C:\FbProfiles\acc01` 就固化下来了，之后程序每次接管都读这一份，登录态持久保留。

再建第二个账号就换目录名 `acc02`，同一条命令换掉路径即可。开发机上另有 `build\new-profile.cmd acc02` 能生成双击启动器，但**它不在发布包里，云主机上没有**。

> 程序启动时会自动带上 `--remote-debugging-port`，端口按账号顺序分配（acc01→9222、acc02→9223），你不用填。

## 拿比特窗口 ID（bit 通道）

1. 装好比特浏览器并登录（本机目前**未安装**，54345 端口无响应，需要先装）
2. 在客户端里新建窗口，登录对应 Facebook 账号
3. 窗口列表里复制该窗口的 **ID**（32 位十六进制），粘到 `比特窗口ID` 列

注意：比特浏览器必须处于**运行状态**，程序才能通过 `127.0.0.1:54345` 连上它。关掉客户端，bit 通道的任务会直接失败。

## 一张可直接照抄的表

只填一个 google 账号、代理留空（直连）的最小版本：

```csv
别名,通道,user-data-dir,比特窗口ID,代理,主页ID,主页名称,凭证引用,状态,备注
acc01,google,C:\FbProfiles\acc01,,,,,cred://acc01,待接入,第一个号
```

## 填完自检三条

1. 手动双击 `C:\FbProfiles\acc01` 对应的 Chrome 快捷方式，**打开 facebook.com 仍是登录态**——这条过不了，后面的自动化全是白跑。
2. 两个账号的目录**不是同一个**，且都不是 `C:\Users\...\AppData\Local\Google\Chrome\User Data`。
3. CSV 里**没有任何密码明文**，`凭证引用` 列只是名字。

## 放哪里

模板在 `docs/accounts.sample.csv`（入库，只有占位数据）。

真实清单放在**程序所在目录**：`C:\FbAuto\accounts.csv`（和 `accounts.sample.csv` 同级，程序只读这一份）。

开发机上放仓库外的 `profiles\accounts.csv`（已 gitignore）；云主机上 `C:\FbAuto\` 本身不在任何仓库里，直接放即可。
