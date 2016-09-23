
print("hello base")

local CURRENT_MODULE_NAME = ...

print(CURRENT_MODULE_NAME)

local PACKAGE_NAME = string.sub(CURRENT_MODULE_NAME, 1, -6)
print(PACKAGE_NAME)
