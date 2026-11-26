# Política de assinatura do cmdwatch

Configuração a ser criada no painel do SignPath.io quando a Fundação aprovar o
projeto. O que a Fundação exige, e o que este arquivo deixa pronto.

## Signing policy (a política que assina as releases)

| Campo | Valor |
|---|---|
| Nome (slug) | `release-signing` |
| Projeto | `cmdwatch` |
| Descrição | `Assina o executável e os artefatos de release do cmdwatch` |
| Certificado | certificado de code signing da SignPath Foundation (em nome da SignPath Foundation) |
| Aprovação | **manual, obrigatória** — um Approver aprova cada pedido antes da assinatura |
| Aprovadores | `dellanio` (único mantenedor do repositório) |

A aprovação manual por release não é escolha: está nos termos da Fundação
("Every release needs manual approval for signing"). O workflow
`.github/workflows/release.yml` envia o pedido e **fica esperando** — um
`Approver` aprova no painel do SignPath e o binário assinado volta para o CI.

## Artifact configuration

Conteúdo pronto em [`artifact-configuration.xml`](artifact-configuration.xml).
Ele diz ao SignPath exatamente o que assinar e impõe a metadata do arquivo:

- `product-name="cmdwatch"` — nome do produto em todas as builds
- `product-version="${version}"` — mesma versão em todas as builds, enviada
  pelo workflow
- `authenticode-sign` — assinatura Authenticode embutida (SHA-256)

## Pipeline policies (recomendado)

Restrições de build que o SignPath aplica ao avaliar o pedido. Cole em
*Signing Policy → Pipeline Policies*:

```yaml
github-build-policies:
  version: '1.0'
  disallow_reruns: false
  runners:
    require_github_hosted: true
```

`require_github_hosted: true` garante que o build do binário assinado veio de
runner hospedado do GitHub — que é exatamente o que o `.github/workflows/`
faz.

## Orgulho zero: o que ainda falta (depende de ações humanas)

1. **A Fundação liberar o projeto** — a aplicação é feita pelo dono do
   repositório em <https://signpath.org/apply>.
2. **App do SignPath instalado no repositório** — <https://github.com/apps/signpath>
   (necessário para a auditoria de origem).
3. **Conectar o projeto ao Trusted Build System "GitHub.com"** no painel.
4. **Secret e variáveis no GitHub** (Settings → Secrets and variables → Actions):

   | Tipo | Nome | Valor |
   |---|---|---|
   | Secret | `SIGNPATH_API_TOKEN` | token de API de um usuário com permissão de submissão |
   | Variável | `SIGNPATH_ORGANIZATION_ID` | organization id do SignPath |
   | Variável | `SIGNPATH_PROJECT_SLUG` | slug do projeto no SignPath |
   | Variável | `SIGNPATH_SIGNING_POLICY_SLUG` | `release-signing` |

   Sem `SIGNPATH_ORGANIZATION_ID` o passo de assinatura é ignorado e a release
   sai sem assinatura — o projeto continua funcionando como hoje.
