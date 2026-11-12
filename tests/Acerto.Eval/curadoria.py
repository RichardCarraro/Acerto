# Curadoria dos erros comuns minerados do corpus (saída de "AcertoEval data comuns").
# Uso: python curadoria.py <comuns.tsv> <correcoes.txt>
# Acrescenta ao correcoes.txt só os erros aprovados na revisão manual de 2026-11-12:
#  - inversão, letra dobrada, tecla vizinha, tecla a mais: listas de aprovados escritas à mão abaixo;
#  - letra faltando: aceita quando a letra que falta é do MEIO da palavra (primeira/última são ambíguas:
#    "muit" é muito ou muita?) e o erro não está na lista de exclusão (palavras de outros idiomas ou ambíguas).
import io, sys

tsv, out = sys.argv[1], sys.argv[2]

KEEP = set("""
vcoê apra praa paar isos masi mutio esotu vamso minah pdoe memso asism notie orbigado estamso calro pessaos pocuo
qaul trablaho alguam garnde dinehiro dinherio qaunto qualuqer nosos descuple ceretza probelma relamente realemnte
aocnteceu primerio deveira alguams primeria demias ciranças crinaças siginfica útlima crinaça colcoar prórpio prórpia
deonte precebi quissese difícies interia queito consoco
vvocê voocê voccê paara paraa eestá esstá issso issoo commo comoo aquii maiss muuito minnha entãoo fazerr tennho
eestava dissse dizzer possso asssim essse esssa mellhor senhorr estee fiicar pesssoas precisso traballho precissa
querria commigo nossso fiilho fillho mullher carrro vennha famíília ollhe duass laado ladoo paree precissamos fiique
ggente pesssoa legall errrado esssas difíícil essses fiica fillha signifiica morrrer ollhos passsar fiicou terrra
tivessse sufiiciente connheço fiinal neele fiilme ceddo ficco umass dóllares aalto millhões asssassino assasssino
menssagem beeber aniverssário pudder fiilmes deever interresse preeto quissesse imprenssa interressa discursso
cocê vicê oara eatá isao issi issp vomo nuito camos tudi mimha emtão elea certp nads mesno aasim assin ninca numca
vocêa fabor nelhor temis senpre semore precido nimguém ninguén sabet dinheito putra primeito boltar
nemhum nenhun emquanto homems garora oerto signigica núsica nemhuma nenhuna veljo estavan sufuciente matoi dormit
probavelmente minuti direiro escritótio fututo bontade legemdas inbecil comfortável sujeiro ruims ordems boms tambén
poden deven queren tenhan fossen chaman pensan
""".split())

# alvo certo quando o minerador escolheu outro
FIX = {
    "descobir": "descobrir", "conheçi": "conheci", "algums": "alguns", "bixo": "bicho", "valew": "valeu",
    "inglêsa": "inglesa", "polícial": "policial", "daquí": "daqui", "voçês": "vocês", "algúem": "alguém",
    "tráz": "traz", "trêm": "trem", "albúm": "álbum", "êles": "eles", "têem": "têm", "veêm": "veem", "deêm": "deem",
    "poderam": "puderam", "enquando": "enquanto", "tentanto": "tentando", "parace": "parece",
    "simplismente": "simplesmente", "interesante": "interessante", "siginifica": "significa",
    "extamente": "exatamente", "acontencendo": "acontecendo", "dançe": "dance", "protejer": "proteger",
    "nãoo": "não", "lincença": "licença", "assitir": "assistir", "tudos": "todos", "tter": "ter", "enão": "então",
    "perai": "peraí", "entâo": "então", "entäo": "então", "díficil": "difícil", "côco": "coco", "vocé": "você",
    "óbviamente": "obviamente", "estáva": "estava", "provávelmente": "provavelmente", "stamos": "estamos",
    "asim": "assim", "nehum": "nenhum", "nehuma": "nenhuma", "tavez": "talvez", "agete": "agente", "própio": "próprio",
    "própia": "própria", "assasino": "assassino", "assasinato": "assassinato",
}

# letra faltando no meio, mas é palavra de outro idioma ou tem mais de uma leitura
SKIP_MISSING = set("""
etre chee compar aiga lndo crito lete carles tchu impresa podríamos madou queo feiz
quano nuna nite precio podria quato otro otra otros otras cereza fente pessa demas gosa prar vino peder acar pasado
pasada pasando pasar pasei pasou pono questo moros ropa ganar crecer pixe memo aida camino primera primero primeros
tercero tercera manera deseo creo odia clase campamento interesa interesado interese proceso asunto locura robar
asistente impresionante verdadero verdadera parcero profesor cellar hava tena form foam lire preo salar cime plas
peas aude meste garda gardas cornel hame deressa taga trou intera brana malco bado viru laur flam inte eino dees
soos ento estu toos tems anes dier auda loge olar hamar hamo hamam nete mese jove senora senores senorita futbol
bixo ender morr dari camia tatro socal nist funds terms costs plans dents lores supe fier rvore rvores scuro spada
avalos echar omou ober spero juda cabou migos omens emais orte apai ltima contecendo ausa hance omer iver onto
thau enry heny lube clue lanos entil aver ropa senor verdad
""".split())

rows = [l.rstrip("\n").split("\t") for l in io.open(tsv, encoding="utf-8") if l.strip()]
existing = {l.split("\t")[0] for l in io.open(out, encoding="utf-8") if "\t" in l}
added = {}
for kind, typo, word, _count in rows:
    if typo in existing or typo in added:
        continue
    if typo in FIX:
        added[typo] = FIX[typo]
    elif kind == "letra faltando":
        if typo == word[1:] or typo == word[:-1] or typo in SKIP_MISSING:
            continue
        added[typo] = word
    elif typo in KEEP:
        added[typo] = word
for typo, word in FIX.items():
    if typo not in existing and typo not in added:
        added[typo] = word

with io.open(out, "a", encoding="utf-8", newline="\n") as f:
    for typo, word in added.items():
        f.write(f"{typo}\t{word}\n")
print(len(added), "entradas acrescentadas")
