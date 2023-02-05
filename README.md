# 初めに

これは [FakeByte/EpicOnlineTranport](https://github.com/FakeByte/EpicOnlineTransport) のForkリポジトリです。  
元リポジトリが最新のMirrorの更新に対応していない為、こちらで対応をしています。  
元リポジトリではEOS SDKを含めていましたが、C#SDKのUnity対応は[sim-mokomo/eos_plugin_for_unity](https://github.com/sim-mokomo/eos_plugin_for_unity)に任せているため、  
このリポジトリでは削除をしています。

# 利用方法

アセット `Mirror` を import 後、`Mirror/Transport` ディレクトリ直下に本リポジトリのサブモジュールを作成。

```
git submodule add git@github.com:sim-mokomo/EpicOnlineTransport.git
```

`Mirror.Transports.asmdef` の Assembly Definition References に `Mirror.Transport.EpicOnlineTransport` を追加。 

詳しい実装内容は元のリポジトリのREADMEを参照してください。
