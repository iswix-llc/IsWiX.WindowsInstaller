#!/usr/bin/env bash
# Regenerates the test MSI fixtures with msibuild (package: msitools).
#   basic.msi                 - small, committed; checked by the unit tests.
#   generated/stress.msi      - 11 MB, not committed; covers 3-byte string refs,
#                               >64 KB strings, mini and regular streams, and a
#                               FAT that needs a DIFAT chain. Tests skip it if absent.
set -euo pipefail
cd "$(dirname "$0")"

msibuild basic.msi -i idt/Property.idt -i idt/File.idt -i idt/Binary.idt -a Binary.hello expected/hello.bin

mkdir -p generated && cd generated
python3 - <<'PY'
import hashlib, random
random.seed(7)
N = 70000
with open('Stress.idt', 'w', newline='') as f:
    f.write('Id\tValue\tNum\ns72\tl0\ti4\nStress\tId\n')
    for i in range(N):
        val = 'v%d' % i
        if i == 5: val = 'L' * 70000        # forces a string over 64 KB
        if i == 6: val = 'café ünï'
        f.write('k%d\t%s\t%d\n' % (i, val, (i * 7919) % 100000 - 50000))
with open('Blob.idt', 'w', newline='') as f:
    f.write('Name\tData\ns72\tv0\nBlob\tName\nbig\t\nsmall\t\n')
rng = random.Random(11)
big = bytes(rng.getrandbits(8) for _ in range(9_000_000))
open('big.bin', 'wb').write(big)
open('small.bin', 'wb').write(b'tiny payload \x00\x01\x02')
open('expected_big_sha256.txt', 'w').write(hashlib.sha256(big).hexdigest().upper() + '\n')
PY
msibuild stress.msi -i Stress.idt -i Blob.idt -a Blob.big big.bin -a Blob.small small.bin
rm -f Stress.idt Blob.idt big.bin small.bin
echo "generated stress.msi and basic.msi"
