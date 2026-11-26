# SignPath Foundation — assinatura do cmdwatch

O que a Fundação exige, o que já está pronto no repositório e o que ainda
depende de ação humana.

## O que é

A [SignPath Foundation](https://signpath.org/) é uma ONG sem fins lucrativos
mantida pela SignPath.io que emite **certificados de code signing gratuitos**
para projetos open source que cumpriam as condições dos
[termos](https://signpath.org/terms.html). O certificado sai em nome da
**SignPath Foundation** — é ela que aparece como *publisher* no SmartScreen —
e a assinatura acontece num pipeline verificado, com **aprovação manual de
cada release**.

Alternativas já avaliadas e descartadas: Azure Artifact Signing (não elegível
para o Brasil), certificado OV público com HSM (US$ 150–300/ano) e MSIX do
Microsoft Store (contradiz o produto portátil).

## Estado atual

| Item | Situação |
|---|---|
| Licença OSI | ✅ MIT |
| Release já publicada no formato a assinar | ✅ v1.3.2 (`cmdwatch.exe` portátil) |
| Funcionalidade documentada | ✅ README en + pt-BR |
| Política de assinatura na home page | ✅ seção “Code signing policy” nos dois READMEs, com a frase exigida, os papéis e a privacidade |
| Metadata do arquivo (`ProductName` / `ProductVersion`) | ✅ gerada pelo `build.ps1` a partir de `src/Program.cs`; o build falha se faltar |
| Build verificável | ✅ `.github/workflows/build.yml` compila em runner hospedado do GitHub e confere a metadata |
| Pipeline de assinatura | ✅ `.github/workflows/release.yml` — compila, assina (se houver credenciais), e publica a release |
| Configuração de artefato | ✅ [`artifact-configuration.xml`](artifact-configuration.xml) |
| Política de assinatura | ✅ [`signing-policy.md`](signing-policy.md) |
| Rascunho da aplicação | ✅ [`application.md`](application.md) |
| **Aprovação da Fundação** | ⏳ **pendente** — aplicação em <https://signpath.org/apply> |
| **2FA do GitHub** | ⏳ confirmar (exigido pelos termos) |

## Como funciona o fluxo

```
tag vX.Y.Z
   │
   ├─ build.ps1  ──► cmdwatch.exe  (ProductName=cmdwatch, ProductVersion=X.Y.Z)
   │
   ├─ upload-artifact (GitHub)  ──► pedido de assinatura no SignPath
   │                                    │
   │                                    └─ Approver aprova no painel
   │                                          │
   │                                   binário assinado volta pro CI
   │
   └─ gh release create  ──► cmdwatch.exe (assinado) + cmdwatch.exe.sha256
```

## O que depende de você

1. **Submeter a aplicação** em <https://signpath.org/apply> — precisa da conta
   do dono do repositório (`dellanio`) e da sua assinatura nos termos.
2. **Ativar 2FA no GitHub** — os termos exigem MFA para todos do time.
3. Depois da aprovação: instalar o [app do SignPath](https://github.com/apps/signpath)
   no repositório, criar o projeto/política/configuração no painel e cadastrar
   o secret `SIGNPATH_API_TOKEN` e as três variáveis no GitHub
   (detalhes em [`signing-policy.md`](signing-policy.md)).

## Verificação

```powershell
# o binário publicado está assinado?
Get-AuthenticodeSignature .\out\cmdwatch.exe | Select-Object Status, SignerCertificate

# a metadata que o SignPath exige está lá?
[Diagnostics.FileVersionInfo]::GetVersionInfo('.\out\cmdwatch.exe') |
  Select-Object ProductName, ProductVersion, FileVersion
```

O certificado da Fundação aparece como *publisher* **SignPath Foundation** — não
como “dellanio”. É a contrapartida de um certificado gratuito: o nome da
Fundação fica no SmartScreen no lugar do seu.
