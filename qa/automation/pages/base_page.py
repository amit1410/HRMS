"""Base class for Page Objects: thin wrapper over a Playwright Page with Allure-annotated actions."""

from __future__ import annotations

import allure
from playwright.sync_api import Page


class BasePage:
    def __init__(self, page: Page):
        self.page = page

    @allure.step("Navigate to {url}")
    def goto(self, url: str) -> "BasePage":
        self.page.goto(url)
        return self

    def attach_screenshot(self, name: str = "state") -> None:
        allure.attach(self.page.screenshot(full_page=True), name=name, attachment_type=allure.attachment_type.PNG)
