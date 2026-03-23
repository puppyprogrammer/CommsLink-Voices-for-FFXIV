#!/bin/bash
# FFXIVoices Phase 4 — Voice Mapping Tests
# Run with server active on localhost:3000
# No set -e: we use || fail patterns that rely on non-zero exit codes

BASE="http://localhost:3000"
GREEN='\033[0;32m'
RED='\033[0;31m'
CYAN='\033[0;36m'
NC='\033[0m'

PASS=0
FAIL=0

pass() { echo -e "${GREEN}PASS${NC}: $1"; PASS=$((PASS + 1)); }
fail() { echo -e "${RED}FAIL${NC}: $1"; FAIL=$((FAIL + 1)); }
section() { echo -e "\n${CYAN}--- $1 ---${NC}"; }

section "1. GET /voices — list overrides"

RESP=$(curl -s "$BASE/voices")
echo "$RESP" | grep -q '"available"' && pass "GET /voices returns available voices" || fail "GET /voices returns available voices"
echo "$RESP" | grep -q '"overrides"' && pass "GET /voices returns overrides" || fail "GET /voices returns overrides"

section "2. PUT /voices — add overrides"

RESP=$(curl -s -X PUT "$BASE/voices" -H "Content-Type: application/json" \
  -d '{"byContentId":{"TEST_CID_001":"male"},"byPlayerName":{"Test Paladin":"female"}}')
echo "$RESP" | grep -q '"status":"updated"' && pass "PUT /voices accepted" || fail "PUT /voices accepted"

# Verify the override persists in GET
RESP=$(curl -s "$BASE/voices")
echo "$RESP" | grep -q 'TEST_CID_001' && pass "contentId override persisted" || fail "contentId override persisted"
echo "$RESP" | grep -q 'Test Paladin' && pass "playerName override persisted" || fail "playerName override persisted"

section "3. PUT /voices — validation rejects bad voice"

RESP=$(curl -s -X PUT "$BASE/voices" -H "Content-Type: application/json" \
  -d '{"byPlayerName":{"Bad Entry":"soprano"}}')
echo "$RESP" | grep -q '"error"' && pass "Invalid voice rejected" || fail "Invalid voice rejected"

section "4. GET /voices/resolve — resolution priority"

# Test contentId override takes priority
RESP=$(curl -s "$BASE/voices/resolve?playerName=Test+Paladin&contentId=TEST_CID_001")
echo "$RESP" | grep -q '"voice":"male"' && pass "contentId override takes priority" || fail "contentId override takes priority"
echo "$RESP" | grep -q '"source":"contentId"' && pass "source=contentId reported" || fail "source=contentId reported"

# Test playerName override (no contentId match)
RESP=$(curl -s "$BASE/voices/resolve?playerName=Test+Paladin&contentId=UNKNOWN")
echo "$RESP" | grep -q '"voice":"female"' && pass "playerName fallback works" || fail "playerName fallback works"
echo "$RESP" | grep -q '"source":"playerName"' && pass "source=playerName reported" || fail "source=playerName reported"

# Test hash fallback (no overrides match)
RESP=$(curl -s "$BASE/voices/resolve?playerName=Nobody+Special")
echo "$RESP" | grep -q '"source":"hash"' && pass "hash fallback used for unknown player" || fail "hash fallback used for unknown player"

# Test hash parity: "AB" has charCode 65+66=131 (odd) → female
RESP=$(curl -s "$BASE/voices/resolve?playerName=AB")
echo "$RESP" | grep -q '"voice":"female"' && pass "Hash parity: AB→female (odd sum)" || fail "Hash parity: AB→female (odd sum)"

# Test hash parity: "AC" has charCode 65+67=132 (even) → male
RESP=$(curl -s "$BASE/voices/resolve?playerName=AC")
echo "$RESP" | grep -q '"voice":"male"' && pass "Hash parity: AC→male (even sum)" || fail "Hash parity: AC→male (even sum)"

section "5. POST /chat — voice override reflected in response"

RESP=$(curl -s -X POST "$BASE/chat" -H "Content-Type: application/json" \
  -d '{"playerName":"Test Paladin","contentId":"UNKNOWN","message":"Shield wall!"}')
echo "$RESP" | grep -q '"voice":"female"' && pass "POST /chat uses playerName override" || fail "POST /chat uses playerName override"

RESP=$(curl -s -X POST "$BASE/chat" -H "Content-Type: application/json" \
  -d '{"playerName":"Test Paladin","contentId":"TEST_CID_001","message":"Ready check!"}')
echo "$RESP" | grep -q '"voice":"male"' && pass "POST /chat uses contentId override" || fail "POST /chat uses contentId override"

section "6. DELETE /voices — remove overrides"

RESP=$(curl -s -X DELETE "$BASE/voices" -H "Content-Type: application/json" \
  -d '{"byContentId":["TEST_CID_001"],"byPlayerName":["Test Paladin"]}')
echo "$RESP" | grep -q '"removed"' && pass "DELETE /voices accepted" || fail "DELETE /voices accepted"

# Verify removed
RESP=$(curl -s "$BASE/voices")
echo "$RESP" | grep -q 'TEST_CID_001' && fail "contentId override not cleaned up" || pass "contentId override cleaned up"
echo "$RESP" | grep -q 'Test Paladin' && fail "playerName override not cleaned up" || pass "playerName override cleaned up"

section "7. Edge cases"

# Missing playerName param
RESP=$(curl -s "$BASE/voices/resolve")
echo "$RESP" | grep -q '"error"' && pass "GET /voices/resolve without playerName returns error" || fail "resolve without playerName"

# Invalid JSON on PUT
RESP=$(curl -s -X PUT "$BASE/voices" -H "Content-Type: application/json" -d 'not json')
echo "$RESP" | grep -q '"error"' && pass "PUT /voices rejects invalid JSON" || fail "PUT rejects invalid JSON"

# Summary
echo ""
echo "=============================="
echo -e "Results: ${GREEN}${PASS} passed${NC}, ${RED}${FAIL} failed${NC}"
echo "=============================="

if [ "$FAIL" -gt 0 ]; then
  exit 1
fi
