#!/bin/bash
# FFXIVoices smoke tests — run with server active on localhost:3000
set -e

BASE="http://localhost:3000"
GREEN='\033[0;32m'
RED='\033[0;31m'
NC='\033[0m'

pass() { echo -e "${GREEN}PASS${NC}: $1"; }
fail() { echo -e "${RED}FAIL${NC}: $1"; exit 1; }

echo "=== FFXIVoices Smoke Tests ==="

# Test 1: Health endpoint
HEALTH=$(curl -s "$BASE/health")
echo "$HEALTH" | grep -q '"status":"ok"' && pass "GET /health" || fail "GET /health"

# Test 2: POST /chat with valid data
RESP=$(curl -s -X POST "$BASE/chat" -H "Content-Type: application/json" \
  -d '{"playerName":"Smoke Test","contentId":"0","message":"Hello world"}')
echo "$RESP" | grep -q '"status":"queued"' && pass "POST /chat valid" || fail "POST /chat valid"

# Test 3: POST /chat missing fields
RESP=$(curl -s -X POST "$BASE/chat" -H "Content-Type: application/json" \
  -d '{"playerName":""}')
echo "$RESP" | grep -q '"error"' && pass "POST /chat validation" || fail "POST /chat validation"

# Test 4: 404 on unknown route
RESP=$(curl -s "$BASE/unknown")
echo "$RESP" | grep -q '"error":"Not found"' && pass "404 handler" || fail "404 handler"

# Test 5: Voice mapping parity
RESP=$(curl -s -X POST "$BASE/chat" -H "Content-Type: application/json" \
  -d '{"playerName":"AA","message":"even hash test"}')
echo "$RESP" | grep -q '"voice":"male"' && pass "Voice parity (even=male)" || fail "Voice parity"

echo ""
echo "=== All tests passed ==="
