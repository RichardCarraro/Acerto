# Gera o roteiro com o tempo de cada tecla (tiktok-roteiro.js) e a trilha (assets/tiktok-som.wav).
# Fonte única de tempos: a animação lê o .js, então som e imagem não se desencontram.
# Uso: python tiktok_roteiro.py   (só precisa de numpy)
import json, wave
from pathlib import Path
import numpy as np

AQUI = Path(__file__).parent
SR = 48000
FIM = 32.0
BPM = 120

# ---------- roteiro ----------
FRASE = [  # d = digitado, c = como fica, tag = o que o Acertô fez, depois = corrige quando a próxima palavra termina
    {"d": "voce", "c": "Você", "tag": "Maiúscula"},
    {"d": "viu"}, {"d": "a"},
    {"d": "informacao?", "c": "informação?", "tag": "Acento"},
    {"d": "isso"},
    {"d": "e", "c": "é", "tag": "Contexto", "depois": True},
    {"d": "mutio", "c": "muito", "tag": "Digitação"},
    {"d": "importante"},
]
TROCAS = [("concerteza", "com certeza"), ("hje", "hoje"), ("joao", "João"), ("vlw", "valeu"), ("as vezes", "às vezes")]
CHIPS = ["Navegador", "Word", "Teams", "WhatsApp", "Bloco de Notas", "E-mail"]

sons = []  # (tempo, nome)

def frase(t0, letra=0.07, pausa=0.12):
    out, t, pendente = [], t0, None
    for p in FRASE:
        w = {**p, "letras": []}
        for _ in p["d"]:
            t += letra; w["letras"].append(round(t, 3)); sons.append((t, "tecla"))
        t += letra; w["espaco"] = round(t, 3); sons.append((t, "espaco"))
        if pendente is not None:  # "e" vira "é" quando "muito" termina
            pendente["fix"] = w["espaco"]; pendente = None
        if "c" in p:
            if p.get("depois"): pendente = w
            else: w["fix"] = w["espaco"]
        t += pausa
        out.append(w)
    for w in out:
        if "fix" in w: sons.append((w["fix"] + 0.02, "ding"))
    return out, round(t, 3)

def trocas(t0, passo=1.1, letra=0.05):
    out = []
    for i, (d, c) in enumerate(TROCAS):
        t = t0 + i * passo
        letras = [round(t + 0.12 + k * letra, 3) for k in range(len(d))]
        for x in letras: sons.append((x, "tecla"))
        fix = round(letras[-1] + 0.2, 3)
        sons.append((fix, "espaco")); sons.append((fix + 0.02, "ding"))
        out.append({"d": d, "c": c, "t": round(t, 3), "letras": letras, "fix": fix})
    return out

def apaga(t0, palavra="mutio", letra=0.13):
    tempos = [round(t0 + k * letra, 3) for k in range(len(palavra))]
    for x in tempos: sons.append((x, "tecla"))
    return {"palavra": palavra, "tempos": tempos}

def chips(t0, passo=0.22):
    tempos = [round(t0 + i * passo, 3) for i in range(len(CHIPS))]
    for x in tempos: sons.append((x, "pop"))
    return [{"nome": n, "t": x} for n, x in zip(CHIPS, tempos)]

roteiro = {
    "fim": FIM,
    "apaga": apaga(3.4),
    "frase": frase(8.6)[0],
    "trocas": trocas(15.2),
    "chips": chips(21.4),
}
sons += [(0.3, "thud"), (1.3, "erro"), (4.95, "whoosh"), (5.35, "pop"), (5.35, "impacto"),
         (14.9, "whoosh"), (20.75, "whoosh"), (24.1, "whoosh"), (27.7, "whoosh"), (28.05, "impacto")]
(AQUI / "tiktok-roteiro.js").write_text(
    "// gerado por tiktok_roteiro.py: não edite à mão\nwindow.ROTEIRO = " + json.dumps(roteiro, ensure_ascii=False) + ";\n",
    encoding="utf-8")

# ---------- síntese ----------
rng = np.random.default_rng(7)

def env(n, ataque, queda):
    t = np.arange(n) / SR
    return np.minimum(1, t / max(ataque, 1e-4)) * np.exp(-t * queda)

def passa_baixa(x, corte):
    a = np.exp(-2 * np.pi * corte / SR)
    y = np.empty_like(x); acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc; y[i] = acc
    return y

def passa_alta(x, corte):
    return x - passa_baixa(x, corte)

def seno(f, dur, fase=0):
    t = np.arange(int(dur * SR)) / SR
    if callable(f):
        return np.sin(2 * np.pi * np.cumsum(f(t)) / SR + fase)
    return np.sin(2 * np.pi * f * t + fase)

def tecla():
    n = int(0.035 * SR)
    clique = passa_alta(rng.standard_normal(n), 1800) * env(n, 0.0005, 160)
    corpo = seno(rng.uniform(260, 340), 0.035) * env(n, 0.0005, 120) * 0.6
    return (clique * 0.5 + corpo) * rng.uniform(0.75, 1)

def espaco():
    n = int(0.06 * SR)
    return (passa_alta(rng.standard_normal(n), 900) * env(n, 0.001, 90) * 0.45 + seno(180, 0.06) * env(n, 0.001, 70) * 0.7)

def ding():
    d = 0.7; n = int(d * SR)
    return (seno(1568, d) + 0.5 * seno(2352, d) + 0.25 * seno(3136, d)) * env(n, 0.002, 7) * 0.35

def pop():
    d = 0.09; n = int(d * SR)
    return seno(lambda t: 380 + 900 * t / d, d) * env(n, 0.001, 45) * 0.6

def whoosh():
    d = 0.55; n = int(d * SR); t = np.arange(n) / SR
    r = passa_alta(passa_baixa(rng.standard_normal(n), 3500), 400)
    return r * np.sin(np.pi * t / d) ** 2 * 0.35

def thud():
    d = 0.5; n = int(d * SR)
    return seno(lambda t: 45 + 110 * np.exp(-t * 18), d) * env(n, 0.001, 9) * 0.95

def impacto():
    d = 1.2; n = int(d * SR)
    grave = seno(lambda t: 40 + 90 * np.exp(-t * 12), d) * env(n, 0.001, 4.5)
    ar = passa_baixa(rng.standard_normal(n), 2500) * env(n, 0.001, 6) * 0.25
    return (grave + ar) * 0.8

def erro():
    d = 0.28; n = int(d * SR); t = np.arange(n) / SR
    q = np.sign(np.sin(2 * np.pi * 140 * t)) + np.sign(np.sin(2 * np.pi * 147 * t))
    return passa_baixa(q, 1200) * env(n, 0.003, 10) * 0.18

SINTESES = {"tecla": tecla, "espaco": espaco, "ding": ding, "pop": pop, "whoosh": whoosh,
            "thud": thud, "impacto": impacto, "erro": erro}

N = int(FIM * SR)
efeitos = np.zeros(N)
for t, nome in sons:
    s = SINTESES[nome](); i = int(t * SR)
    efeitos[i:i + len(s)] += s[: max(0, N - i)]

# batida: Lá menor, Fá, Dó, Sol; entra na revelação do logo
batida = 5.0; beat = 60 / BPM; compasso = 4 * beat
ACORDES = [(220.0, 261.63, 329.63), (174.61, 220.0, 261.63), (130.81, 164.81, 196.0), (196.0, 246.94, 293.66)]
t_all = np.arange(N) / SR
pad = np.zeros(N); baixo = np.zeros(N); bumbo = np.zeros(N); chimbal = np.zeros(N); palma = np.zeros(N)
for k, acorde in enumerate(ACORDES * 8):
    ini = k * compasso
    if ini >= FIM: break
    i0, i1 = int(ini * SR), min(N, int((ini + compasso) * SR))
    t = (np.arange(i1 - i0)) / SR
    e = np.minimum(1, t / 0.3) * np.minimum(1, (compasso - t) / 0.3)
    for f in acorde:
        for det in (0.997, 1.003):
            pad[i0:i1] += (np.sin(2 * np.pi * f * det * t) + 0.3 * np.sin(4 * np.pi * f * det * t)) * e
    for b in range(8):  # baixo em colcheias
        j0 = i0 + int(b * beat / 2 * SR); m = min(int(beat / 2 * SR), N - j0)
        if m <= 0: break
        tb = np.arange(m) / SR
        baixo[j0:j0 + m] += np.sin(2 * np.pi * acorde[0] / 2 * tb) * np.exp(-tb * 6)
pad = passa_baixa(pad, 1400) * 0.06
n_beats = int(FIM / beat)
ganho_pad = np.ones(N)
for b in range(n_beats):
    tb = b * beat
    if tb < batida: continue
    i = int(tb * SR)
    k = thud(); bumbo[i:i + len(k)] += k[: N - i] * 0.8
    m = min(int(beat * SR), N - i); ganho_pad[i:i + m] = 1 - 0.6 * np.exp(-np.arange(m) / SR * 9)  # sidechain
    j = int((tb + beat / 2) * SR)
    if j < N:
        h = passa_alta(rng.standard_normal(int(0.03 * SR)), 6000) * env(int(0.03 * SR), 0.0005, 120) * 0.25
        chimbal[j:j + len(h)] += h[: N - j]
    if b % 2 == 1:
        c = passa_baixa(passa_alta(rng.standard_normal(int(0.15 * SR)), 900), 4000) * env(int(0.15 * SR), 0.001, 22) * 0.5
        palma[i:i + len(c)] += c[: N - i]
baixo[: int(batida * SR)] = 0
musica = pad * ganho_pad + passa_baixa(baixo, 500) * 0.35 + bumbo * 0.55 + chimbal + palma * 0.6
musica[: int(batida * SR)] *= np.linspace(0.3, 1, int(batida * SR))  # só o pad antes da batida
musica *= np.clip((FIM - t_all) / 1.5, 0, 1)  # sai no final

mix = musica * 0.55 + efeitos * 0.9
mix = np.tanh(mix * 1.2) / np.tanh(1.2)  # segura picos sem estourar
mix *= 0.89 / np.max(np.abs(mix))
pcm = (np.column_stack([mix, mix]) * 32767).astype(np.int16)
with wave.open(str(AQUI / "assets" / "tiktok-som.wav"), "wb") as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(pcm.tobytes())

assert roteiro["frase"][5]["fix"] == roteiro["frase"][6]["fix"], "o 'é' deve corrigir junto com 'muito'"
print("ok:", len(sons), "sons; frase termina em", roteiro["frase"][-1]["espaco"], "s")
