import http from 'k6/http';
import { check, sleep } from 'k6';

// Configuração do teste
export const options = {
  // Estágios de carga para simular diferentes níveis de utilizadores
  stages: [
    { duration: '30s', target: 5 },    // Ramp-up para 5 utilizadores em 30s
    { duration: '1m', target: 10 },    // Ramp-up para 10 utilizadores em 1m
    { duration: '30s', target: 20 },   // Ramp-up para 20 utilizadores em 30s
    { duration: '1m', target: 20 },    // Estabilizar em 20 utilizadores por 1m
    { duration: '30s', target: 0 },    // Ramp-down para 0 utilizadores
  ],
  thresholds: {
    http_req_duration: ['p(95)<500'], // 95% das requisições devem completar abaixo de 500ms
    http_req_failed: ['rate<0.01'],   // Menos de 1% pode falhar
  },
};


// Função principal do teste
export default function() {
  // Obter o catálogo de produtos (opcional, apenas para verificação)
  const catalogResponse = http.get('http://localhost:5222/api/catalog/items?api-version=1.0');
  check(catalogResponse, {
    'catálogo carregado com sucesso': (r) => r.status === 200,
  });

  // Pausa antes da próxima iteração
  sleep(Math.random() * 3 + 1); // 1-4 segundos
}