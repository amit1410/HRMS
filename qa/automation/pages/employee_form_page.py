"""Page Object for the Employee → Personal Details tab
(Frontend/HRMS.Web/src/pages/employees/PersonalDetailsForm.tsx), shared by the create route
(`/employees/new`) and the edit/detail route (`/employees/{id}`, personal tab active by default).
"""

from __future__ import annotations

import allure

from .base_page import BasePage


class EmployeeFormPage(BasePage):
    FIRST_NAME = "#firstName"
    LAST_NAME = "#lastName"
    DATE_OF_JOINING = "#dateOfJoining"
    FIRST_NAME_ERROR = "#firstName-error"
    LAST_NAME_ERROR = "#lastName-error"
    DATE_OF_JOINING_ERROR = "#dateOfJoining-error"
    SUBMIT_BUTTON = "form button[type='submit']"
    ERROR_NOTICE = ".notice-error"
    SUCCESS_ALERT = ".employee-alert"
    EMPLOYEE_CODE_VALUE = ".employee-code-value"
    PERSONAL_TAB = "#tab-personal"
    PAGE_HEADER = ".page-header, h1"

    @allure.step("Open employee form: {url}")
    def open(self, url: str) -> "EmployeeFormPage":
        self.goto(url)
        self.page.wait_for_selector(self.FIRST_NAME, timeout=15_000)
        return self

    @allure.step("Fill minimum required Personal Details")
    def fill_minimum_required(self, first_name: str, last_name: str, date_of_joining_iso: str) -> "EmployeeFormPage":
        self.page.locator(self.FIRST_NAME).fill(first_name)
        self.page.locator(self.LAST_NAME).fill(last_name)
        self.page.locator(self.DATE_OF_JOINING).fill(date_of_joining_iso)
        return self

    @allure.step("Clear required Personal Details fields")
    def clear_required_fields(self) -> "EmployeeFormPage":
        self.page.locator(self.FIRST_NAME).fill("")
        self.page.locator(self.LAST_NAME).fill("")
        return self

    @allure.step("Submit the Personal Details form")
    def submit(self) -> "EmployeeFormPage":
        self.page.locator(self.SUBMIT_BUTTON).click()
        return self

    def first_name_value(self) -> str:
        return self.page.locator(self.FIRST_NAME).input_value()

    def last_name_value(self) -> str:
        return self.page.locator(self.LAST_NAME).input_value()

    def employee_code_text(self) -> str | None:
        locator = self.page.locator(self.EMPLOYEE_CODE_VALUE)
        return locator.first.inner_text() if locator.count() > 0 else None

    def has_field_errors(self) -> bool:
        return self.page.locator(self.FIRST_NAME_ERROR).count() > 0 or self.page.locator(self.LAST_NAME_ERROR).count() > 0

    def success_message(self) -> str | None:
        locator = self.page.locator(self.SUCCESS_ALERT)
        return locator.first.inner_text() if locator.count() > 0 else None
