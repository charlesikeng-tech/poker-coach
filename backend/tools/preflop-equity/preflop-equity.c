// Preflop all-in equity between the 169 starting-hand classes, Monte Carlo.
// Output: 169x169 little-endian uint16, equity of row vs column in 1/10000 (ties count half).
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>
#include <string.h>

static uint64_t rng = 0x9E3779B97F4A7C15ULL;
static inline uint64_t next(void){ rng ^= rng << 13; rng ^= rng >> 7; rng ^= rng << 17; return rng; }
static inline int rnd(int n){ return (int)((next() >> 11) % (uint64_t)n); }

// card = rank*4 + suit, rank 0..12 = 2..A
static int straight_high(int mask){ // mask bit r = rank r present; returns high rank of best straight or -1
    int m = (mask << 1) | ((mask >> 12) & 1); // bit0 = ace low
    for (int hi = 13; hi >= 4; hi--) { int w = 0x1F << (hi - 4); if ((m & w) == w) return hi - 1; }
    return -1;
}
static int eval7(const int *c){
    int cnt[13] = {0}, suitmask[4] = {0}, suitcnt[4] = {0}, mask = 0;
    for (int i = 0; i < 7; i++){ int r = c[i] >> 2, s = c[i] & 3; cnt[r]++; suitmask[s] |= 1 << r; suitcnt[s]++; mask |= 1 << r; }
    for (int s = 0; s < 4; s++) if (suitcnt[s] >= 5) {
        int sh = straight_high(suitmask[s]);
        if (sh >= 0) return (8 << 20) | sh;
        int v = 0, k = 0;
        for (int r = 12; r >= 0 && k < 5; r--) if (suitmask[s] & (1 << r)) { v = (v << 4) | r; k++; }
        return (5 << 20) | v;
    }
    int quad = -1, trips[2] = {-1,-1}, nt = 0, pairs[3] = {-1,-1,-1}, np = 0;
    for (int r = 12; r >= 0; r--) {
        if (cnt[r] == 4) quad = r;
        else if (cnt[r] == 3) { if (nt < 2) trips[nt++] = r; }
        else if (cnt[r] == 2) { if (np < 3) pairs[np++] = r; }
    }
    if (quad >= 0) { int k = -1; for (int r = 12; r >= 0; r--) if (r != quad && cnt[r]) { k = r; break; } return (7 << 20) | (quad << 4) | k; }
    if (nt >= 1 && (nt >= 2 || np >= 1)) { int p = nt >= 2 ? trips[1] : pairs[0]; if (nt >= 2 && np >= 1 && pairs[0] > p) p = pairs[0]; return (6 << 20) | (trips[0] << 4) | p; }
    int sh = straight_high(mask);
    if (sh >= 0) return (4 << 20) | sh;
    if (nt >= 1) { int v = trips[0], k = 0; for (int r = 12; r >= 0 && k < 2; r--) if (cnt[r] == 1) { v = (v << 4) | r; k++; } return (3 << 20) | v; }
    if (np >= 2) { int k = -1; for (int r = 12; r >= 0; r--) if (cnt[r] && r != pairs[0] && r != pairs[1]) { k = r; break; } return (2 << 20) | (pairs[0] << 8) | (pairs[1] << 4) | k; }
    if (np == 1) { int v = pairs[0], k = 0; for (int r = 12; r >= 0 && k < 3; r--) if (cnt[r] == 1) { v = (v << 4) | r; k++; } return (1 << 20) | v; }
    int v = 0, k = 0; for (int r = 12; r >= 0 && k < 5; r--) if (cnt[r]) { v = (v << 4) | r; k++; } return v;
}

// class index in grid order: row/col 0 = ace; pairs on diagonal, suited above (row<col).
static int combos[169][12][2], ncombo[169];
static void build(void){
    for (int i = 0; i < 169; i++){
        int row = i / 13, col = i % 13; int n = 0;
        int hi = 12 - (row < col ? row : col), lo = 12 - (row < col ? col : row);
        if (row == col) { for (int a = 0; a < 4; a++) for (int b = a + 1; b < 4; b++) { combos[i][n][0] = hi*4+a; combos[i][n][1] = hi*4+b; n++; } }
        else if (row < col) { for (int s = 0; s < 4; s++) { combos[i][n][0] = hi*4+s; combos[i][n][1] = lo*4+s; n++; } }
        else { for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++) if (a != b) { combos[i][n][0] = hi*4+a; combos[i][n][1] = lo*4+b; n++; } }
        ncombo[i] = n;
    }
}

static double equity(int i, int j, int samples){
    double win = 0; int done = 0;
    while (done < samples) {
        int *h = combos[i][rnd(ncombo[i])], *v = combos[j][rnd(ncombo[j])];
        if (h[0]==v[0]||h[0]==v[1]||h[1]==v[0]||h[1]==v[1]) continue;
        int deck[52], n = 0; for (int c = 0; c < 52; c++) if (c!=h[0]&&c!=h[1]&&c!=v[0]&&c!=v[1]) deck[n++] = c;
        int a[7], b[7];
        for (int k = 0; k < 5; k++) { int r = k + rnd(n - k); int t = deck[k]; deck[k] = deck[r]; deck[r] = t; a[k] = b[k] = deck[k]; }
        a[5]=h[0]; a[6]=h[1]; b[5]=v[0]; b[6]=v[1];
        int x = eval7(a), y = eval7(b);
        win += x > y ? 1.0 : x == y ? 0.5 : 0.0; done++;
    }
    return win / samples;
}

int main(int argc, char **argv){
    int samples = argc > 1 ? atoi(argv[1]) : 20000;
    build();
    if (argc > 2) { // spot checks: i j
        for (int k = 2; k + 1 < argc; k += 2) printf("%s %s %.4f\n", argv[k], argv[k+1], equity(atoi(argv[k]), atoi(argv[k+1]), samples));
        return 0;
    }
    static uint16_t m[169][169];
    for (int i = 0; i < 169; i++) {
        for (int j = i; j < 169; j++) {
            double e = i == j ? 0.5 : equity(i, j, samples);
            m[i][j] = (uint16_t)(e * 10000 + 0.5); m[j][i] = (uint16_t)(10000 - m[i][j]);
        }
        fprintf(stderr, "%d\n", i);
    }
    fwrite(m, sizeof m, 1, stdout);
    return 0;
}
