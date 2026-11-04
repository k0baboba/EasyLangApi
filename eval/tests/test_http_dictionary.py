import unittest

from easylang_eval import mock_server
from easylang_eval.dataset import Item
from easylang_eval.explainers import DictionaryExplainer
from easylang_eval.http_client import HttpClient, ServiceError, join_url
from easylang_eval.prompts import EMPTY_CONTEXT, PhraseContext


def item(phrase: str, line: str) -> Item:
    return Item("t1", "slang", phrase, "Title", "S", ("a", "b"), line, ("c", "d"), "x", ("y",))


class HttpClientTests(unittest.TestCase):
    def setUp(self):
        server, self.state = mock_server.start(fail_every=2)
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        host, port = server.server_address
        self.url = f"http://{host}:{port}/v1/chat/completions"
        self.body = {"model": "m", "messages": [{"role": "system", "content": "You grade"},
                                                {"role": "user", "content": "Explanation:\nx\n\nCandidates:\n1. x"}]}

    def test_retries_temporary_failures_with_backoff(self):
        pauses = []
        http = HttpClient(retries=2, backoff=0.5)
        http.sleep = pauses.append
        self.assertEqual(http.request("POST", self.url, self.body).status, 200)   # 1-й запрос — 200
        self.assertEqual(http.request("POST", self.url, self.body).status, 200)   # 2-й — 503, повтор — 200
        self.assertEqual(pauses, [0.5])

    def test_gives_up_after_retries(self):
        http = HttpClient(retries=0, backoff=0)
        http.request("POST", self.url, self.body)
        with self.assertRaises(ServiceError) as ctx:
            http.request("POST", self.url, self.body)
        self.assertEqual(ctx.exception.status, 503)

    def test_join_url(self):
        self.assertEqual(join_url("http://h:1/v1/", "chat/completions"), "http://h:1/v1/chat/completions")
        self.assertEqual(join_url("http://h:1/v1", "/chat/completions"), "http://h:1/v1/chat/completions")


class DictionaryLogicTests(unittest.TestCase):
    ENTRIES = [{"meanings": [
        {"partOfSpeech": "noun", "definitions": [
            {"definition": "The land alongside a river."},
            {"definition": "A financial institution that keeps money."},
            {"definition": "A third one, never shown."},
        ]},
        {"partOfSpeech": "verb", "definitions": [{"definition": " "}, {"definition": "To tilt an aircraft."}]},
    ]}]

    def test_to_senses_takes_two_nonempty_per_meaning(self):
        senses = DictionaryExplainer.to_senses("bank", self.ENTRIES)
        self.assertEqual([s["definition"] for s in senses], [
            "The land alongside a river.", "A financial institution that keeps money.", "To tilt an aircraft."])

    def test_rank_by_context_prefers_shared_long_words(self):
        senses = DictionaryExplainer.to_senses("bank", self.ENTRIES)
        ctx = PhraseContext(current_line="I put my money in the bank.")
        self.assertEqual(DictionaryExplainer.rank_by_context(senses, ctx)[0]["definition"],
                         "A financial institution that keeps money.")
        # Без контекста порядок словаря сохраняется.
        self.assertEqual(DictionaryExplainer.rank_by_context(senses, EMPTY_CONTEXT), senses)

    def test_multiword_phrase_falls_back_to_words_and_is_not_found(self):
        server, _ = mock_server.start()
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        host, port = server.server_address
        d = DictionaryExplainer(HttpClient(retries=0), f"http://{host}:{port}/api/v2/entries/en/")
        # «spill the tea» словарь-заглушка не знает целиком, но знает слово «salty» — проверим оба пути.
        self.assertFalse(d.explain(item("spill the tea", "Spill the tea now.")).found)
        a = d.explain(item("salty", "He is salty."))
        self.assertTrue(a.ok and a.found)
        self.assertEqual(a.text, "солёный на вкус (пересолен)")

    def test_5xx_on_multiword_phrase_means_not_found(self):
        server, state = mock_server.start(dictionary_5xx_for_phrases=True)
        self.addCleanup(server.server_close)
        self.addCleanup(server.shutdown)
        host, port = server.server_address
        d = DictionaryExplainer(HttpClient(retries=3, backoff=0), f"http://{host}:{port}/api/v2/entries/en/")
        a = d.explain(item("spill the tea", "Spill the tea now."))
        self.assertTrue(a.ok)
        self.assertFalse(a.found)
        # На выражение — один запрос без повторов, дальше по словам длиннее двух букв (как в C#: Length > 2).
        paths = [r["path"] for r in state.requests]
        self.assertEqual(paths, ["/api/v2/entries/en/spill%20the%20tea", "/api/v2/entries/en/spill",
                                 "/api/v2/entries/en/the", "/api/v2/entries/en/tea"])


if __name__ == "__main__":
    unittest.main()
