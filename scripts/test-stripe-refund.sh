#!/usr/bin/env bash

set -euo pipefail

script_directory="$(
    cd "$(dirname "${BASH_SOURCE[0]}")" &&
        pwd
)"

TOKLONG_STRIPE_TEST_REFUND=true \
    exec "${script_directory}/test-stripe-payment.sh"
