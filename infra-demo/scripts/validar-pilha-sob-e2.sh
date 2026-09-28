#!/usr/bin/env bash
# Valida a pilha da demonstracao sob emulacao do shape Always Free VM.Standard.E2.1.Micro.
#
# Uso:
#   bash infra-demo/scripts/validar-pilha-sob-e2.sh                                  # so o teto de CPU compartilhada
#   bash infra-demo/scripts/validar-pilha-sob-e2.sh infra-demo/compose.e2-pior-caso.yml pior-caso
#
# Exige infra-demo/.env.demo preenchido. A saida vai para infra-demo/.saida-validacao/ (ignorada
# pelo git); TORRE_SAIDA muda o destino.
set -u
RAIZ="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$RAIZ" || exit 1
unset DOCKER_HOST
export MSYS_NO_PATHCONV=1
SP="${TORRE_SAIDA:-$RAIZ/infra-demo/.saida-validacao}"
mkdir -p "$SP"
EXTRA="${1:-}"
ROTULO="${2:-generoso}"
dc() { if [ -n "$EXTRA" ]; then docker compose -f infra-demo/docker-compose.demo.yml -f "$RAIZ/infra-demo/compose.e2-cpu-compartilhada.yml" -f "$EXTRA" --env-file infra-demo/.env.demo "$@"; else docker compose -f infra-demo/docker-compose.demo.yml -f "$RAIZ/infra-demo/compose.e2-cpu-compartilhada.yml" --env-file infra-demo/.env.demo "$@"; fi; }

echo "### 0. Estado limpo  (perfil: $ROTULO)"
dc down >/dev/null 2>&1
docker run --rm -v /srv/torre-logistica:/dados alpine:3 sh -c '
  rm -rf /dados/postgres /dados/comprovantes
  mkdir -p /dados/postgres /dados/comprovantes
  chown -R 999:999 /dados/postgres; chown -R 1654:1654 /dados/comprovantes
  chmod 700 /dados/postgres; chmod 750 /dados/comprovantes' >/dev/null 2>&1
echo "  disco zerado"

echo ""
echo "### 1. Tetos renderizados"
dc config 2>/dev/null | grep -E 'cpuset|memory:' | sed 's/^/  /' | head -12

echo ""
echo "### 2. Subida cronometrada"
inicio=$SECONDS
dc up -d banco api workers web >/dev/null 2>&1
fim=$((SECONDS + 420)); pronto=""
while [ $SECONDS -lt $fim ]; do
  dc ps --format '{{.Service}}:{{.Health}}' 2>/dev/null | grep -q '^api:healthy' && { pronto=sim; break; }
  sleep 3
done
echo "  API saudavel em $((SECONDS - inicio))s ${pronto:+(ok)}"
[ -z "$pronto" ] && { echo "  NAO FICOU SAUDAVEL"; dc logs --tail 25 api; exit 1; }

echo ""
echo "### 3. Repouso"
sleep 10
docker stats --no-stream --format '  {{.Name}}\t{{.MemUsage}}\t{{.CPUPerc}}' torre-demo-banco torre-demo-api torre-demo-workers torre-demo-web

echo ""
echo "### 4. Seis historias, com amostragem a cada 3s"
( while true; do
      docker stats --no-stream --format '{{.Name}} {{.MemUsage}} {{.CPUPerc}}' 2>/dev/null \
        | grep -E 'torre-demo|simulador'
    echo "---"; sleep 3
  done ) > "$SP/amostras-e2-$ROTULO.txt" 2>&1 &
amostrador=$!
inicio=$SECONDS
dc --profile roteiro run --rm simulador > "$SP/simulador-e2-$ROTULO.log" 2>&1
codigo=$?; duracao=$((SECONDS - inicio))
sleep 6; kill $amostrador 2>/dev/null
echo "  simulador: saida=$codigo em ${duracao}s"
grep -E "terminou em|historia|Historia" "$SP/simulador-e2-$ROTULO.log" | sed 's/^.*INF\] /  /' | cut -c1-100 | head -12

echo ""
echo "### 5. Pico"
python - "$SP/amostras-e2-$ROTULO.txt" <<'PY'
import sys, re
pm, pc = {}, {}
for l in open(sys.argv[1], encoding="utf-8", errors="replace"):
    p = l.split()
    if len(p) < 3 or not p[0].startswith("torre-demo-"): continue
    m = re.match(r"([0-9.]+)([KMG])iB", p[1])
    if m: pm[p[0]] = max(pm.get(p[0],0), float(m.group(1))*{"K":1/1024,"M":1,"G":1024}[m.group(2)])
    c = re.match(r"([0-9.]+)%", p[-1])
    if c: pc[p[0]] = max(pc.get(p[0],0), float(c.group(1)))
tm=tc=0.0
for n in sorted(pm):
    print("  %-24s %7.1f MiB   CPU pico %6.1f%%" % (n, pm[n], pc.get(n,0))); tm+=pm[n]; tc+=pc.get(n,0)
print("  %-24s %7.1f MiB   CPU somada %5.1f%%  (100%% = 1 nucleo)" % ("TOTAL (pico por servico)", tm, tc))
PY

echo ""
echo "### 6. O que ficou no banco"
q(){ dc exec -T banco psql -U torre -d torre_logistica -tAc "$1" 2>/dev/null | tr -d '\015'; }
echo "  entregas=$(q 'select count(*) from entregas;') eventos=$(q 'select count(*) from eventos_da_entrega;') posicoes=$(q 'select count(*) from posicoes;')"
echo "  atuais=$(q 'select count(*) from posicoes_atuais;') alertas=$(q 'select count(*) from alertas_operacionais;') ocorrencias=$(q 'select count(*) from ocorrencias;') comprovantes=$(q 'select count(*) from comprovantes;')"
echo "  PostGIS: $(q "select 'geofence 299m=' || ST_DWithin('POINT(-46.6333 -23.5505)'::geography, ST_Project('POINT(-46.6333 -23.5505)'::geography,299,0)::geography,300) || ' 301m=' || ST_DWithin('POINT(-46.6333 -23.5505)'::geography, ST_Project('POINT(-46.6333 -23.5505)'::geography,301,0)::geography,300);")"

echo ""
echo "### 7. Estabilidade / OOM"
for c in banco api workers web; do
  n="torre-demo-$c"
  printf '  %-9s reinicios=%s estado=%s OOMKilled=%s\n' "$c" \
    "$(docker inspect $n --format '{{.RestartCount}}' 2>/dev/null)" \
    "$(docker inspect $n --format '{{.State.Status}}' 2>/dev/null)" \
    "$(docker inspect $n --format '{{.State.OOMKilled}}' 2>/dev/null)"
done
echo "  erros api=$(dc logs api 2>/dev/null | grep -ciE '\[ERR\]|\[FTL\]|Unhandled') workers=$(dc logs workers 2>/dev/null | grep -ciE '\[ERR\]|\[FTL\]|Unhandled')"
