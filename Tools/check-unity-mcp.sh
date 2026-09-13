#!/usr/bin/env bash
# ============================================================================
#  Unity MCP 链路自检
#  ---------------------------------------------------------------------------
#  用途：确认 WorkBuddy ↔ Unity MCP 这条链路是否打通。
#
#  链路结构（三跳，2026-09-12 核实）：
#
#    WorkBuddy 会话
#        ↓   （WorkBuddy 已自动完成，无需人工干预）
#    WorkBuddy MCP 代理   http://127.0.0.1:64544/<hash>/mcp
#        ↓   （HTTP / StreamableHTTP）
#    Python server        http://127.0.0.1:8080/mcp      ← 8080 归它监听
#        ↓   （WebSocket，端口由包自行协商）
#    Unity 编辑器（MCPForUnity 包）
#
#  ⚠ 关键认知：8080 端口是「Python server」开的，不是 Unity 编辑器开的。
#     Unity 侧只需装好 MCPForUnity 包并让它处于运行状态；
#     8080 的启动命令是：
#       uvx --from mcpforunityserver mcp-for-unity --transport http --http-url http://localhost:8080
#     所以「8080 无监听」时，先排查这个 server 进程，而不是去点 Unity 菜单。
#
#  用法：bash WanXiang_Framework/tools/check-unity-mcp.sh
#
#  注意：必须用 --noproxy '*' —— 本机存在 WorkBuddy 沙箱代理（sandbox-cli，
#        127.0.0.1:64584），不加这个参数 curl 会绕经代理，得到误导性的 502
#        或 HTTP 000，而不是真实状态。
# ============================================================================

set -u

PASS=0
FAIL=0
PORT_OK=0

ok()   { echo "  [OK]   $1"; PASS=$((PASS + 1)); }
bad()  { echo "  [FAIL] $1"; FAIL=$((FAIL + 1)); }
info() { echo "  [--]   $1"; }

echo "=============================================="
echo " Unity MCP 链路自检"
echo "=============================================="

# ---------------------------------------------------------------------------
echo ""
echo "1/6  WorkBuddy 侧配置"
# ---------------------------------------------------------------------------
CFG="$HOME/.workbuddy/mcp.json"
if [ -f "$CFG" ]; then
    ok "配置文件存在：$CFG"
    if grep -qi '"unity' "$CFG" 2>/dev/null; then
        ok "已声明 unity MCP 条目"
        info "官方形式：{ \"url\": \"http://localhost:8080/mcp\" }（键名大小写不影响）"
    else
        bad "配置里找不到 unity 相关条目，需在 mcpServers 下添加"
    fi
else
    bad "配置文件不存在：$CFG"
fi

# ---------------------------------------------------------------------------
echo ""
echo "2/6  server 运行依赖（uvx）"
# ---------------------------------------------------------------------------
if command -v uvx >/dev/null 2>&1; then
    ok "uvx 可用：$(uvx --version 2>/dev/null | head -1)"
else
    bad "uvx 未安装 —— Python server 依赖它（https://docs.astral.sh/uv/）"
fi

# ---------------------------------------------------------------------------
echo ""
echo "3/6  git 代理健康（Unity 装包时必踩）"
# ---------------------------------------------------------------------------
# git 的配置文件优先级【高于】http_proxy 环境变量，
# 所以 shell 里代理是对的，Unity 调 git 时仍可能走一个已失效的端口。
GIT_PROXY=$(git config --global --get http.proxy 2>/dev/null || echo "")
if [ -z "$GIT_PROXY" ]; then
    info "git 未配置代理（走环境变量或直连）"
else
    GIT_PROXY_PORT=$(echo "$GIT_PROXY" | sed -E 's#^[a-zA-Z]+://##; s#/*$##' | sed -E 's#.*:##')
    if [ -n "$GIT_PROXY_PORT" ] && netstat -ano 2>/dev/null | grep -i listen | grep -qE ":$GIT_PROXY_PORT[[:space:]]"; then
        ok "git 代理 $GIT_PROXY 端口正在监听"
    else
        bad "git 代理配置为 $GIT_PROXY，但该端口【无服务】"
        info "症状：Unity 报 Failed to connect to 127.0.0.1 port $GIT_PROXY_PORT"
        info "修复（把 <端口> 换成你代理软件实际在用的端口）："
        info "  git config --global http.proxy  http://127.0.0.1:<端口>"
        info "  git config --global https.proxy http://127.0.0.1:<端口>"
        info "或删掉配置改用环境变量："
        info "  git config --global --unset http.proxy; git config --global --unset https.proxy"
    fi
fi

# 列出本机正在监听的回环端口，便于比对「代理到底在哪个端口」
ACTIVE_PORTS=$(netstat -ano 2>/dev/null \
    | grep -i listen \
    | grep -oE "127\.0\.0\.1:[0-9]+" \
    | sort -u -t: -k2 -n \
    | head -15 \
    | tr '\n' ' ')
if [ -n "$ACTIVE_PORTS" ]; then
    info "本机正在监听的 127.0.0.1 端口：$ACTIVE_PORTS"
fi

# ---------------------------------------------------------------------------
echo ""
echo "4/6  Unity 编辑器进程"
# ---------------------------------------------------------------------------
UNITY_COUNT=$(tasklist 2>/dev/null | grep -c "Unity.exe" || echo 0)
if [ "$UNITY_COUNT" -gt 0 ]; then
    ok "检测到 $UNITY_COUNT 个 Unity.exe 进程"
    if [ "$UNITY_COUNT" -gt 1 ]; then
        info "注意：多实例时用 set_active_instance 路由；HTTP 模式下实例 ID 为 Name@hash"
    fi
else
    bad "没有 Unity 进程 —— 需要打开目标工程"
fi

# 顺带看一眼有没有 Python server 进程
SERVER_COUNT=$(tasklist 2>/dev/null | grep -icE "uv\.exe|python\.exe" || echo 0)
if [ "$SERVER_COUNT" -gt 0 ]; then
    info "检测到 $SERVER_COUNT 个 uv/python 进程（可能包含正在下载依赖的 server）"
fi

# ---------------------------------------------------------------------------
echo ""
echo "5/6  8080 端口监听（关键检查项）"
# ---------------------------------------------------------------------------
if netstat -ano 2>/dev/null | grep -i listen | grep -qE ":8080[[:space:]]"; then
    ok "8080 正在监听，Python server 已启动"
    PORT_OK=1
else
    bad "8080 无监听 —— Python server 没跑起来"
    info "启动命令（首次运行需下载依赖，约 1–3 分钟）："
    info "  uvx --from mcpforunityserver mcp-for-unity --transport http --http-url http://localhost:8080"
    info "注意：不要写成 uvx mcpforunityserver，必须用 --from 指定包名。"
fi

# ---------------------------------------------------------------------------
echo ""
echo "6/6  MCP 端点响应"
# ---------------------------------------------------------------------------
if [ "$PORT_OK" -eq 1 ]; then
    CODE=$(curl --noproxy '*' -s -m 5 -o /dev/null -w "%{http_code}" \
        -X POST "http://127.0.0.1:8080/mcp" \
        -H "Content-Type: application/json" \
        -H "Accept: application/json, text/event-stream" \
        -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"wb-probe","version":"1.0"}}}' \
        2>/dev/null)

    if [ "$CODE" = "200" ]; then
        ok "MCP initialize 返回 200，链路完全打通"
    elif [ "$CODE" = "000" ]; then
        bad "端点无响应（连接被拒或超时）"
    else
        info "端点返回 HTTP $CODE（非 200 也可能说明服务在跑，看 Unity 窗口的连接状态）"
    fi
else
    info "跳过（8080 未监听）"
fi

# ---------------------------------------------------------------------------
echo ""
echo "=============================================="
if [ "$FAIL" -eq 0 ]; then
    echo " 结论：链路就绪"
else
    echo " 结论：$FAIL 项未通过（$PASS 项通过）"
    echo ""
    echo " Unity 侧未装包时，三条安装路径（GitHub 不通时用后两条）："
    echo "  A. git URL（需代理可用）"
    echo "     https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main"
    echo "  B. OpenUPM（走 HTTPS CDN，绕开 git 与代理）"
    echo "     manifest.json 加 scopedRegistries → https://package.openupm.com"
    echo "     scope: com.coplaydev，依赖: com.coplaydev.unity-mcp"
    echo "  C. 本地安装（完全离线）"
    echo "     浏览器下载仓库 zip → 解压 → ADD PACKAGE FROM DISK → MCPForUnity/package.json"
fi
echo "=============================================="

exit 0
