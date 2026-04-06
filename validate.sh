#!/bin/bash
# validate.sh — Run Unity in batch mode to validate pose retargeting pipeline
#
# Usage:
#   ./validate.sh          # Run validation
#   ./validate.sh --show   # Just show the last report
#
# Requires Unity 2020.3.48f1 installed via Unity Hub

set -e

PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"
UNITY_APP="/Applications/Unity/Hub/Editor/2020.3.48f1/Unity.app/Contents/MacOS/Unity"
REPORT_PATH="$PROJECT_DIR/validation_report.json"
LOG_PATH="$PROJECT_DIR/validation_unity.log"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

if [ "$1" = "--show" ]; then
    if [ -f "$REPORT_PATH" ]; then
        cat "$REPORT_PATH"
    else
        echo "No report found at $REPORT_PATH"
    fi
    exit 0
fi

# Check Unity exists
if [ ! -f "$UNITY_APP" ]; then
    echo -e "${RED}ERROR: Unity not found at $UNITY_APP${NC}"
    echo "Please update UNITY_APP in this script to match your Unity installation."
    exit 1
fi

# Remove old report
rm -f "$REPORT_PATH"

echo -e "${YELLOW}=== Running Pose Validation ===${NC}"
echo "Project: $PROJECT_DIR"
echo "Unity:   $UNITY_APP"
echo ""

# Run Unity in batch mode
echo "Starting Unity batch mode (this may take 30-60 seconds)..."
"$UNITY_APP" \
    -batchmode \
    -projectPath "$PROJECT_DIR" \
    -executeMethod PoseValidator.Run \
    -logFile "$LOG_PATH" \
    -quit \
    2>&1 || true

# Check if report was generated
if [ ! -f "$REPORT_PATH" ]; then
    echo -e "${RED}ERROR: Validation report not generated.${NC}"
    echo "Check Unity log at: $LOG_PATH"
    echo ""
    echo "Last 30 lines of Unity log:"
    tail -30 "$LOG_PATH" 2>/dev/null || echo "(no log file)"
    exit 1
fi

# Parse and display results
echo ""
echo -e "${YELLOW}=== Validation Report ===${NC}"
echo ""

# Extract key fields using python (available on macOS)
python3 -c "
import json, sys

with open('$REPORT_PATH') as f:
    r = json.load(f)

if r.get('status') == 'error':
    print(f'  ERROR: {r[\"error\"]}')
    sys.exit(1)

print(f'  Scene:            {r.get(\"scene\", \"?\")}')
print(f'  CSV:              {r.get(\"csv\", \"?\")}')
print(f'  Frames processed: {r.get(\"frames_processed\", 0)}')
print(f'  Frames validated: {r.get(\"frames_validated\", 0)}')
print()
print('  Bone Direction Errors (degrees):')
print('  ' + '-' * 50)

bone_errors = r.get('bone_errors_deg', {})
for name, stats in bone_errors.items():
    mean = stats.get('mean', 0)
    mx = stats.get('max', 0)
    indicator = '  OK' if mean < 30 else ' BAD' if mean < 60 else ' !!!'
    print(f'    {name:15s}  mean={mean:5.1f}  max={mx:5.1f} {indicator}')

print()
verdict = r.get('verdict', '?')
worst = r.get('worst_bone', '?')
worst_err = r.get('worst_mean_error_deg', 0)

if verdict == 'PASS':
    print(f'  VERDICT: PASS (worst: {worst} at {worst_err:.1f} deg)')
else:
    print(f'  VERDICT: FAIL (worst: {worst} at {worst_err:.1f} deg)')
    print(f'  Threshold: {r.get(\"pass_threshold_deg\", 30)} deg')
"

# Color the verdict line
VERDICT=$(python3 -c "import json; r=json.load(open('$REPORT_PATH')); print(r.get('verdict','?'))")
echo ""
if [ "$VERDICT" = "PASS" ]; then
    echo -e "${GREEN}=== VALIDATION PASSED ===${NC}"
else
    echo -e "${RED}=== VALIDATION FAILED ===${NC}"
    echo "Check full report: $REPORT_PATH"
    echo "Check Unity log:   $LOG_PATH"
fi
