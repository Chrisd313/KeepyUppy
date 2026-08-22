#!/usr/bin/env sh
# Runs the EditMode test suite headless and reports the result.
#
# Usage:   ./test.sh [extra Unity args]
# Env:     UNITY_PATH   override the editor binary
#          RESULTS      override where results.xml is written
#
# Exits non-zero if any test fails, if Unity fails to launch, or if no results
# were produced — so this is safe to gate a commit on.

set -eu

PROJECT_DIR=$(cd "$(dirname "$0")" && pwd)

UNITY_PATH=${UNITY_PATH:-"/c/Program Files/Unity/Hub/Editor/6000.2.6f2/Editor/Unity.exe"}

# NOT Temp/ — that is Unity's own scratch directory and it is wiped on shutdown,
# taking the results file with it.
RESULTS=${RESULTS:-"$PROJECT_DIR/TestResults/results.xml"}
LOG="$PROJECT_DIR/TestResults/test-run.log"

if [ ! -x "$UNITY_PATH" ] && [ ! -f "$UNITY_PATH" ]; then
    echo "Unity not found at: $UNITY_PATH" >&2
    echo "Set UNITY_PATH to your editor binary." >&2
    exit 127
fi

mkdir -p "$(dirname "$RESULTS")"
rm -f "$RESULTS"

# Unity on Windows wants Windows-style paths.
to_native() {
    if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else echo "$1"; fi
}

echo "Running EditMode tests..."

set +e
"$UNITY_PATH" \
    -batchmode \
    -nographics \
    -projectPath "$(to_native "$PROJECT_DIR")" \
    -runTests \
    -testPlatform EditMode \
    -testResults "$(to_native "$RESULTS")" \
    -logFile "$(to_native "$LOG")" \
    "$@"
UNITY_EXIT=$?
set -e

if [ ! -f "$RESULTS" ]; then
    echo "No results written. Unity exited $UNITY_EXIT; last of $LOG:" >&2
    tail -n 40 "$LOG" >&2 || true
    exit 1
fi

# The test-run element carries the totals as attributes on one line.
SUMMARY=$(tr '>' '>\n' < "$RESULTS" | grep -m1 '<test-run')

field() { echo "$SUMMARY" | sed -n "s/.* $1=\"\([^\"]*\)\".*/\1/p"; }

TOTAL=$(field total)
PASSED=$(field passed)
FAILED=$(field failed)
SKIPPED=$(field skipped)
RESULT=$(field result)

echo "total=$TOTAL passed=$PASSED failed=$FAILED skipped=$SKIPPED result=$RESULT"

if [ "${FAILED:-1}" != "0" ] || [ "$RESULT" != "Passed" ]; then
    echo "" >&2
    echo "Failures:" >&2
    grep -o 'fullname="[^"]*"[^>]*result="Failed"' "$RESULTS" >&2 || true
    grep -A3 '<failure>' "$RESULTS" | head -n 60 >&2 || true
    exit 1
fi

exit 0
